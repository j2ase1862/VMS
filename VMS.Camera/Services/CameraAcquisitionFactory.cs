using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// CameraInfo.Manufacturer에 따라 적절한 ICameraAcquisition 구현체를 반환하는 팩토리
    /// </summary>
    public static class CameraAcquisitionFactory
    {
        public static ICameraAcquisition Create(CameraInfo camera)
        {
            var manufacturer = camera.Manufacturer ?? string.Empty;

            if (manufacturer.Contains("Mech", StringComparison.OrdinalIgnoreCase))
            {
#if MECHMIND_AVAILABLE
                return new MechMindCameraAcquisition();
#else
                System.Diagnostics.Debug.WriteLine("Mech-Mind SDK가 설치되지 않았습니다. 시뮬레이션 모드로 전환합니다.");
                return new SimulatedCameraAcquisition();
#endif
            }

            if (manufacturer.Contains("Basler", StringComparison.OrdinalIgnoreCase))
            {
#if BASLER_AVAILABLE
                return new BaslerCameraAcquisition();
#else
                System.Diagnostics.Debug.WriteLine("Basler Pylon SDK가 설치되지 않았습니다. 시뮬레이션 모드로 전환합니다.");
                return new SimulatedCameraAcquisition();
#endif
            }

            if (manufacturer.Contains("Matrox", StringComparison.OrdinalIgnoreCase) ||
                manufacturer.Contains("Dalsa", StringComparison.OrdinalIgnoreCase))
            {
#if MIL_AVAILABLE
                return new MatroxCameraAcquisition();
#else
                System.Diagnostics.Debug.WriteLine("Matrox MIL SDK가 설치되지 않았습니다. 시뮬레이션 모드로 전환합니다.");
                return new SimulatedCameraAcquisition();
#endif
            }

            // 향후 실제 SDK 구현 시 여기에 추가:
            // "HIK" => new HikCameraAcquisition(),

            return new SimulatedCameraAcquisition();
        }
    }
}
