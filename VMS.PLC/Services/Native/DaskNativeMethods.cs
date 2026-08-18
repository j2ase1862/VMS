using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VMS.PLC.Services.Native
{
    /// <summary>
    /// ADLink DASK (Data Acquisition Software Kit) P/Invoke 선언 — PCI-7432/7433/7434 시리즈용.
    ///
    /// <para><b>DLL 이름</b>: ADLink 배포판의 실제 파일명은 <c>PCI-Dask.dll</c>(32-bit) /
    /// <c>PCI-Dask64.dll</c>(64-bit) 이다. 과거 이 파일은 존재하지 않는 <c>dask.dll</c> 을
    /// 하드코딩해 현장에서 항상 DllNotFoundException 이 났다 (2026-08-14, PCI-7432 실증).
    /// 지금은 <see cref="NativeLibrary.SetDllImportResolver"/> 로 후보 이름을 순차 시도한다.</para>
    ///
    /// <para><b>비트수 주의</b>: VMS 는 64-bit 프로세스다. 32-bit <c>PCI-Dask.dll</c> 만 설치된
    /// PC 에서는 어떤 이름을 쓰든 로드할 수 없다 — ADLink DASK <b>x64</b> 드라이버가 필요하다.
    /// 진단을 위해 <see cref="DiagnosticsText"/> 가 시도한 이름과 프로세스 비트수를 남긴다.</para>
    ///
    /// 모든 함수 return code: 0 = NoError, &lt; 0 = 에러 코드 (DASK error code 정의 참조).
    /// </summary>
    internal static class DaskNativeMethods
    {
        /// <summary>DllImport 에 쓰는 논리 이름 — 실제 로드는 resolver 가 후보군에서 고른다.</summary>
        private const string DLL = "PCI-Dask.dll";

        private const CallingConvention CC = CallingConvention.StdCall;

        /// <summary>
        /// 로드 후보 — 64-bit 우선. 현장 검증된 PalletControl(32-bit)은 PCI-Dask.dll 을 쓴다.
        /// dask.dll 은 구버전 호환을 위해 마지막에만 시도.
        /// </summary>
        private static readonly string[] CandidateNames =
        {
            "PCI-Dask64.dll",
            "PCI-Dask.dll",
            "PCIDask64.dll",
            "PCIDask.dll",
            "dask.dll",
        };

        private static readonly List<string> _attempted = new();

        /// <summary>실제로 로드에 성공한 DLL 이름 (실패 시 null).</summary>
        public static string? LoadedLibraryName { get; private set; }

        /// <summary>연결 실패 시 로그에 남길 진단 문구 — 시도한 이름 + 프로세스 비트수.</summary>
        public static string DiagnosticsText =>
            LoadedLibraryName != null
                ? $"loaded='{LoadedLibraryName}', {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} 프로세스"
                : $"시도한 DLL=[{string.Join(", ", CandidateNames)}], " +
                  $"{(Environment.Is64BitProcess ? "64-bit" : "32-bit")} 프로세스 — " +
                  "ADLink DASK 드라이버(프로세스와 같은 비트수) 설치 여부를 확인하세요.";

        static DaskNativeMethods()
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(DaskNativeMethods).Assembly, Resolve);
            }
            catch (InvalidOperationException)
            {
                // 이미 등록됨 — 무시 (동일 어셈블리에 resolver 는 1회만 등록 가능)
            }
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, DLL, StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            foreach (var candidate in CandidateNames)
            {
                if (NativeLibrary.TryLoad(candidate, out var handle))
                {
                    LoadedLibraryName = candidate;
                    Debug.WriteLine($"[DASK] loaded native library '{candidate}'");
                    return handle;
                }
                lock (_attempted) { _attempted.Add(candidate); }
            }

            Debug.WriteLine($"[DASK] native library not found. {DiagnosticsText}");
            return IntPtr.Zero;   // → DllNotFoundException
        }

        // ─── Card type 상수 ───
        // 공식 PCIS-DASK 헤더(Dask64.h/Dask.h) 정의값. 과거 0x11(=17=PCI_7433!)을 쓰다가
        // Register_Card 가 -13(ErrorOpenDriverFailed)으로 실패했다 (2026-08-18, PCI-7432 실증).
        // 0x11 의 출처였던 PalletControl 은 DllNotFoundException 시 조용히 시뮬레이션 모드로
        // 폴백하는 코드라 실기 검증 근거가 못 된다 — ADLink 값은 공식 헤더·샘플만 신뢰할 것.
        public const ushort PCI_7432 = 16;  // 32 isolated DI + 32 isolated DO (2026-08-18 현장 샘플 검증)
        public const ushort PCI_7433 = 17;  // 64 isolated DI only (미검증)
        public const ushort PCI_7434 = 18;  // 64 isolated DO only (미검증)

        // ─── 포트/라인 매핑 ───
        // 743x 계열의 포트는 카드 종류가 아니라 채널 번호로 정해진다: 채널 0~31 = 포트 0
        // (PORT_DI_LOW/PORT_DO_LOW), 채널 32~63 = 포트 1 (HIGH). 방향은 DI_/DO_ 함수가 구분.
        // 공식 샘플 근거 — 7432·7434: DO_WritePort(h, PORT_DO_LOW, …) / 7433: DI 포트 LOW·HIGH.
        // 과거 "7432 는 DO=포트 1" 은 PalletControl 발(發) 오답 — 실기에서 출력이 나가지 않는다.
        public static ushort PortForChannel(int channel) => (ushort)(channel / 32);

        public static ushort LineForChannel(int channel) => (ushort)(channel % 32);

        // ─── Lifecycle ───

        /// <summary>카드 등록. 0 이상이면 성공, 음수면 에러 코드.</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short Register_Card(ushort CardType, ushort card_num);

        /// <summary>카드 해제.</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short Release_Card(ushort card_num);

        // ─── Digital Input (Bit / Port) ───

        /// <summary>단일 비트 read. state: 0 또는 1.</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short DI_ReadLine(ushort card_num, ushort Port, ushort Line, ref ushort state);

        /// <summary>포트 단위 read (32-bit).</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short DI_ReadPort(ushort card_num, ushort Port, ref uint value);

        // ─── Digital Output (Bit / Port) ───

        /// <summary>단일 비트 write.</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short DO_WriteLine(ushort card_num, ushort Port, ushort Line, ushort state);

        /// <summary>포트 단위 write (32-bit).</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short DO_WritePort(ushort card_num, ushort Port, uint value);

        /// <summary>모델명 → CardType 매핑. 알 수 없는 모델은 PCI_7432 기본.</summary>
        public static ushort ModelToCardType(string model)
        {
            return model switch
            {
                "PCI-7432" => PCI_7432,
                "PCI-7433" => PCI_7433,
                "PCI-7434" => PCI_7434,
                _ => PCI_7432
            };
        }

        /// <summary>해당 카드 타입의 상수가 현장 검증됐는지 — 미검증이면 연결 로그에 경고.</summary>
        public static bool IsCardTypeVerified(ushort cardType) => cardType == PCI_7432;
    }
}
