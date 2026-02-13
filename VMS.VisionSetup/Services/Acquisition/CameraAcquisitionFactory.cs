using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services.Acquisition
{
    /// <summary>
    /// CameraInfo.Manufacturer에 따라 적절한 ICameraAcquisition 구현체를 반환하는 팩토리
    /// 실제 SDK 구현체 (Mech-Mind, Basler, HIK 등)는 새 클래스를 만들어 여기에 등록
    /// </summary>
    public static class CameraAcquisitionFactory
    {
        public static ICameraAcquisition Create(CameraInfo camera)
        {
            // 향후 실제 SDK 구현 시 여기에 추가:
            // "Mech-Mind" => new MechMindCameraAcquisition(),
            // "Basler" => new BaslerCameraAcquisition(),
            // "HIK" => new HikCameraAcquisition(),

            return new SimulatedCameraAcquisition();
        }
    }
}
