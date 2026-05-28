using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// Advantech PCI-17xx 시리즈 Mock — 실제 DAQNavi SDK (Automation.BDaq) 미설치 시 사용.
    /// Phase 3 에서 AdvantechDaqNaviConnection (InstantDiCtrl / InstantDoCtrl) 으로 교체 예정.
    /// </summary>
    public class AdvantechMockBoard : IoBoardMockBase
    {
        public AdvantechMockBoard(string deviceId, int inputChannelCount, int outputChannelCount)
            : base(deviceId, IoDeviceType.AdvantechPci17xx, inputChannelCount, outputChannelCount)
        {
        }
    }
}
