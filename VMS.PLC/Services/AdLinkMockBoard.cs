using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// ADLink PCI-743x 시리즈 Mock — 실제 DASK SDK 미설치 시 사용.
    /// Phase 3 에서 AdLinkDaskConnection (P/Invoke DASK DLL) 으로 교체 예정.
    /// </summary>
    public class AdLinkMockBoard : IoBoardMockBase
    {
        public AdLinkMockBoard(string deviceId, int inputChannelCount, int outputChannelCount)
            : base(deviceId, IoDeviceType.AdLinkPci743x, inputChannelCount, outputChannelCount)
        {
        }
    }
}
