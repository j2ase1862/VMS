using VMS.Camera.Models;
using VMS.Camera.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// SDK 미탑재 폴백 노출 (2026-08-11) — 조용한 시뮬레이션 폴백이 현장에서
    /// 실카메라로 오인되던 문제(Basler 사태)의 회귀 방지.
    /// SDK 유무는 빌드 환경마다 다르므로(개발 PC=pylon 있음, CI=없음)
    /// 특정 분기 대신 "폴백 플래그 ↔ 구현체 타입" 일관성을 검증한다.
    /// </summary>
    public class CameraAcquisitionFallbackTests
    {
        [Theory]
        [InlineData("Basler")]
        [InlineData("Mech-Mind")]
        [InlineData("Hikrobot")]
        [InlineData("Matrox")]
        public void KnownManufacturer_FallbackFlag_MatchesImplementation(string manufacturer)
        {
            var creation = CameraAcquisitionFactory.CreateWithInfo(
                new CameraInfo { Manufacturer = manufacturer });

            Assert.NotNull(creation.Acquisition);
            // 시뮬레이션 구현체가 반환됐다면 반드시 폴백으로 표시돼야 한다 (조용한 폴백 금지)
            Assert.Equal(creation.Acquisition is SimulatedCameraAcquisition,
                         creation.IsSimulationFallback);
            if (creation.IsSimulationFallback)
                Assert.False(string.IsNullOrWhiteSpace(creation.FallbackReason));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Simulated")]
        public void UnknownOrVirtualManufacturer_IsIntentionalSimulation_NotFallback(string? manufacturer)
        {
            var creation = CameraAcquisitionFactory.CreateWithInfo(
                new CameraInfo { Manufacturer = manufacturer! });

            Assert.IsType<SimulatedCameraAcquisition>(creation.Acquisition);
            Assert.False(creation.IsSimulationFallback);   // 가상 카메라는 의도된 시뮬레이션
            Assert.Null(creation.FallbackReason);
        }

        [Fact]
        public void LegacyCreate_StillReturnsAcquisition()
        {
            var acq = CameraAcquisitionFactory.Create(new CameraInfo { Manufacturer = "Basler" });
            Assert.NotNull(acq);
        }
    }
}
