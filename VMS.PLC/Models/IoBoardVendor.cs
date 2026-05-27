namespace VMS.PLC.Models
{
    /// <summary>
    /// IO 보드 제조사.
    /// IoBoardConnectionFactory 가 Vendor 별로 적절한 IIoBoardConnection 구현체를 생성.
    /// </summary>
    public enum IoBoardVendor
    {
        None,

        /// <summary>ADLink — PCI-7432 / PCI-7433 / PCI-7434 / PCI-7396 등 (DASK SDK).</summary>
        AdLink,

        /// <summary>Advantech — PCI-1710 / PCI-1714 / PCI-1716 / PCI-1750 / PCI-1751 등 (DAQNavi SDK).</summary>
        Advantech
    }
}
