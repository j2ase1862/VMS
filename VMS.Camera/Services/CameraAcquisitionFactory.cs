using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// 취득 구현체 생성 결과 — SDK 미탑재 폴백 여부를 호출부(UI)에 노출.
    /// 폴백을 조용히 삼키면 Release 현장에서 "실카메라인 줄 알았는데 시뮬레이션"
    /// 사고가 난다 (2026-08 Basler 실증 사태의 원흉 — 백로그 등재 건).
    /// </summary>
    public sealed record CameraAcquisitionCreation(
        ICameraAcquisition Acquisition,
        bool IsSimulationFallback,
        string? FallbackReason);

    /// <summary>
    /// CameraInfo.Manufacturer에 따라 적절한 ICameraAcquisition 구현체를 반환하는 팩토리
    /// </summary>
    public static class CameraAcquisitionFactory
    {
        /// <summary>
        /// 구현체 + 폴백 정보 반환. UI 호출부는 IsSimulationFallback 이면 반드시
        /// 사용자에게 경고를 표시할 것 (시뮬레이션 영상을 실카메라로 오인 방지).
        /// 제조사 미지정/알 수 없음 → 의도된 시뮬레이션(가상 카메라)으로 폴백 아님.
        /// </summary>
        public static CameraAcquisitionCreation CreateWithInfo(CameraInfo camera)
        {
            var manufacturer = camera.Manufacturer ?? string.Empty;

            if (manufacturer.Contains("Mech", StringComparison.OrdinalIgnoreCase))
            {
#if MECHMIND_AVAILABLE
                return new CameraAcquisitionCreation(new MechMindCameraAcquisition(), false, null);
#else
                return Fallback("Mech-Mind SDK");
#endif
            }

            if (manufacturer.Contains("Basler", StringComparison.OrdinalIgnoreCase))
            {
#if BASLER_AVAILABLE
                return new CameraAcquisitionCreation(new BaslerCameraAcquisition(), false, null);
#else
                return Fallback("Basler Pylon SDK");
#endif
            }

            if (manufacturer.Contains("Matrox", StringComparison.OrdinalIgnoreCase) ||
                manufacturer.Contains("Dalsa", StringComparison.OrdinalIgnoreCase))
            {
#if MIL_AVAILABLE
                return new CameraAcquisitionCreation(new MatroxCameraAcquisition(), false, null);
#else
                return Fallback("Matrox MIL SDK");
#endif
            }

            // Hikrobot / Hikvision / HIK
            if (manufacturer.Contains("Hik", StringComparison.OrdinalIgnoreCase))
            {
#if HIK_AVAILABLE
                return new CameraAcquisitionCreation(new HikCameraAcquisition(), false, null);
#else
                return Fallback("Hikrobot MVS SDK");
#endif
            }

            // 제조사 미지정/가상 카메라 — 의도된 시뮬레이션 (폴백 아님)
            return new CameraAcquisitionCreation(new SimulatedCameraAcquisition(), false, null);
        }

        /// <summary>기존 호환 API — 폴백 정보가 필요 없는 호출부용.</summary>
        public static ICameraAcquisition Create(CameraInfo camera)
            => CreateWithInfo(camera).Acquisition;

        private static CameraAcquisitionCreation Fallback(string sdkName)
        {
            var reason = $"{sdkName} 미탑재 빌드 — 시뮬레이션 모드로 동작합니다 (실카메라 영상 아님)";
            System.Diagnostics.Debug.WriteLine($"[CameraAcquisitionFactory] {reason}");
            return new CameraAcquisitionCreation(new SimulatedCameraAcquisition(), true, reason);
        }
    }
}
