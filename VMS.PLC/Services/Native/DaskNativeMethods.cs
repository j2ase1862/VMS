using System.Runtime.InteropServices;

namespace VMS.PLC.Services.Native
{
    /// <summary>
    /// ADLink DASK (Data Acquisition Software Kit) P/Invoke 선언 — PCI-7432/7433/7434 시리즈용.
    ///
    /// DLL: dask.dll (ADLink driver 설치 시 System32 또는 SysWOW64 에 배치).
    /// CSProj 의존성 없음 — 동적 로드. DLL 미발견 시 ConnectAsync 가 DllNotFoundException 잡고
    /// Factory 가 Mock 으로 폴백.
    ///
    /// 함수 시그니처 출처: ADLink DASK manual (PCI-7432/7433/7434 — 32-channel isolated I/O).
    /// 모든 함수 return code: 0 = NoError, &lt; 0 = 에러 코드 (DASK error code 정의 참조).
    ///
    /// 주의: ADLink 가 64-bit 환경에선 dask.dll 명이 유지되지만 내부 코드는 64-bit. 32-bit
    /// 환경과 함수 시그니처 동일.
    /// </summary>
    internal static class DaskNativeMethods
    {
        private const string DLL = "dask.dll";
        private const CallingConvention CC = CallingConvention.StdCall;

        // ─── Card type 상수 (PCI-7432/7433/7434) ───
        public const ushort PCI_7432 = 0x37;  // 32 isolated DI + 32 isolated DO
        public const ushort PCI_7433 = 0x38;  // 32 isolated DI only
        public const ushort PCI_7434 = 0x39;  // 32 isolated DO only

        // ─── Lifecycle ───

        /// <summary>카드 등록 → 0 이상이면 CardId, 음수면 에러.</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short Register_Card(ushort CardType, ushort card_num);

        /// <summary>카드 해제.</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short Release_Card(ushort CardId);

        // ─── Digital Input (Bit / Port) ───

        /// <summary>단일 비트 read. state: 0 또는 1.</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short DI_ReadLine(ushort CardId, ushort Port, ushort Line, ref ushort state);

        /// <summary>포트 단위 read. PCI-7432/7433 는 32-bit isolated DI 단일 포트 (Port=0).</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short DI_ReadPort(ushort CardId, ushort Port, ref uint value);

        // ─── Digital Output (Bit / Port) ───

        /// <summary>단일 비트 write.</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short DO_WriteLine(ushort CardId, ushort Port, ushort Line, ushort state);

        /// <summary>포트 단위 write. PCI-7432/7434 는 32-bit isolated DO 단일 포트 (Port=0).</summary>
        [DllImport(DLL, CallingConvention = CC)]
        public static extern short DO_WritePort(ushort CardId, ushort Port, uint value);

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
    }
}
