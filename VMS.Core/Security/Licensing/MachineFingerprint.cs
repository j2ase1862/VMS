using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// 하드웨어 지문 — 3 구성요소를 각 5자 세그먼트로 인코딩한 "XXXXX-XXXXX-XXXXX" (15자).
    ///
    /// 세그먼트 구성 (spec §4 — 그룹당 요소 1개, 그룹 단위 비교로 2/3 규칙 직결):
    ///   1. Windows MachineGuid (설치 단위 고유 — 재설치/sysprep 시 변경)
    ///   2. 물리 NIC MAC 최솟값 (NIC 교체 시 변경, 열거 순서 무관하게 안정)
    ///   3. CPU 식별 문자열 (모델 단위 — 동일 모델 PC 간 공유되지만,
    ///      나머지 두 요소가 개체 단위라 2/3 규칙상 실질 바인딩은 유지됨.
    ///      반대로 재설치(1 변경)·NIC 교체(2 변경) 각각에 대한 관용을 제공)
    ///
    /// 전화로 불러줄 수 있도록 Crockford Base32 (0/O·1/I 혼동 문자 제외).
    /// 요소 수집 실패 시 해당 세그먼트는 고정 마커 해시 — 두 PC 가 같은 요소를
    /// 못 읽으면 그 세그먼트끼리는 일치하나, 개체 식별은 나머지 요소가 담당.
    /// </summary>
    public static class MachineFingerprint
    {
        private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        public const int SegmentLength = 5;
        public const int SegmentCount = 3;
        public const int RequiredMatches = 2;
        /// <summary>Internal 라이선스 전용 — 지문 무바인딩.</summary>
        public const string Wildcard = "*";

        private static string? _cached;

        /// <summary>현재 머신의 지문 코드 (프로세스 수명 동안 캐시).</summary>
        public static string GetCode() => _cached ??= BuildCode(
            ReadMachineGuid(), ReadPrimaryMacAddress(), ReadCpuIdentity());

        /// <summary>
        /// 라이선스 지문과 머신 지문 비교 — 세그먼트 2/3 이상 일치 시 true.
        /// 형식 불일치(세그먼트 수/길이 다름)는 false.
        /// </summary>
        public static bool Matches(string licenseFingerprint, string machineFingerprint)
        {
            if (licenseFingerprint == Wildcard) return true;

            var lic = licenseFingerprint.Split('-');
            var mac = machineFingerprint.Split('-');
            if (lic.Length != SegmentCount || mac.Length != SegmentCount) return false;

            var matches = 0;
            for (var i = 0; i < SegmentCount; i++)
            {
                if (lic[i].Length != SegmentLength || mac[i].Length != SegmentLength) return false;
                if (string.Equals(lic[i], mac[i], StringComparison.OrdinalIgnoreCase)) matches++;
            }
            return matches >= RequiredMatches;
        }

        /// <summary>요소 문자열 3개 → 지문 코드. 테스트 격리용 공개.</summary>
        internal static string BuildCode(string? machineGuid, string? macAddress, string? cpuIdentity)
        {
            return string.Join("-",
                EncodeSegment("guid", machineGuid),
                EncodeSegment("mac", macAddress),
                EncodeSegment("cpu", cpuIdentity));
        }

        /// <summary>
        /// 요소 → SHA-256 → 상위 25비트 → Crockford Base32 5자.
        /// 수집 실패(null/공백)는 요소명 마커로 해시 — 결정적이되 실제 값과 충돌하지 않음.
        /// </summary>
        private static string EncodeSegment(string componentName, string? value)
        {
            var input = string.IsNullOrWhiteSpace(value)
                ? $"unavailable:{componentName}"
                : $"{componentName}:{value.Trim().ToUpperInvariant()}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

            // 해시 상위 25비트 → 5비트씩 5자
            var bits32 = ((uint)hash[0] << 24) | ((uint)hash[1] << 16) | ((uint)hash[2] << 8) | hash[3];
            var top25 = bits32 >> 7;
            var chars = new char[SegmentLength];
            for (var i = 0; i < SegmentLength; i++)
                chars[i] = CrockfordAlphabet[(int)((top25 >> (5 * (SegmentLength - 1 - i))) & 0x1F)];
            return new string(chars);
        }

        private static string? ReadMachineGuid()
        {
            try
            {
                // 64비트 뷰 강제 — 32비트 프로세스에서도 동일 값 (WOW64 리다이렉션 회피)
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid") as string;
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadPrimaryMacAddress()
        {
            try
            {
                var candidates = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                                                        or NetworkInterfaceType.Wireless80211)
                    .Select(n => n.GetPhysicalAddress().ToString())
                    .Where(m => m.Length == 12)  // 6바이트 MAC 만 (가상/터널 제외 목적)
                    .OrderBy(m => m, StringComparer.Ordinal)
                    .ToList();
                return candidates.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadCpuIdentity()
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = hklm.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key == null) return null;
                var vendor = key.GetValue("VendorIdentifier") as string;
                var identifier = key.GetValue("Identifier") as string;
                var name = key.GetValue("ProcessorNameString") as string;
                var combined = $"{vendor}|{identifier}|{name}";
                return combined == "||" ? null : combined;
            }
            catch
            {
                return null;
            }
        }
    }
}
