namespace VMS.PLC.Models
{
    /// <summary>
    /// IO 보드 제조사.
    /// IoBoardConnectionFactory 가 Vendor 별로 적절한 IIoBoardConnection 구현체를 생성.
    /// </summary>
    public enum IoBoardVendor
    {
        None,

        /// <summary>
        /// ADLink — PCI-7432(현장 검증) / PCI-7433 / PCI-7434 (DASK SDK). 그 밖의 모델(예: PCI-7396)은 카드 형식이
        /// 없어 PCI-7432 로 등록을 시도한다 — 지원 모델 아님(VO-03).
        /// </summary>
        AdLink,

        /// <summary>Advantech — PCI-1710 / PCI-1714 / PCI-1716 / PCI-1750 / PCI-1751 등 (DAQNavi SDK).</summary>
        Advantech
    }
}
