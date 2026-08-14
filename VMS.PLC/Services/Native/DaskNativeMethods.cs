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
        // PCI-7432 = 0x11 은 현장 검증된 PalletControl(Pci7432Device.cs)에서 확인한 값.
        // 7433/7434 는 미검증 — 해당 모델 사용 시 로그로 경고하고, 실패하면 이 값을 먼저 의심할 것.
        public const ushort PCI_7432 = 0x11;  // 32 isolated DI + 32 isolated DO (검증됨)
        public const ushort PCI_7433 = 0x12;  // 32 isolated DI only (미검증)
        public const ushort PCI_7434 = 0x13;  // 32 isolated DO only (미검증)

        // ─── 포트 번호 ───
        // PCI-7432 는 DI 와 DO 가 서로 다른 포트다 (DI=0, DO=1). 과거 코드는 둘 다 0 을 써서
        // 출력이 동작하지 않았다. 7433 은 DI 만, 7434 는 DO 만 있으므로 각각 포트 0.
        public static ushort DiPortFor(ushort cardType) => 0;

        public static ushort DoPortFor(ushort cardType) => cardType switch
        {
            PCI_7432 => 1,
            PCI_7434 => 0,   // DO 전용 카드
            _ => 1
        };

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
