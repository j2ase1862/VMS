using VMS.Camera.Models;
using VMS.Camera.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VO-11: 연결 구현이 없는 제조사(IDS · Cognex · Keyence · Baumer · Allied Vision · FLIR · JAI)를 고르면
    /// 조용히 시뮬레이션으로 붙지 않고 'SDK 미탑재 — 시뮬레이션' 폴백으로 알린다(목록에서는 빼지 않음).
    /// 가상 · 미지정 · 기타 제조사는 그대로 의도된 시뮬레이션(폴백 아님).
    /// </summary>
    public class CameraNoSdkManufacturerTests
    {
        [Theory]
        [InlineData(CameraManufacturer.IDS)]
        [InlineData(CameraManufacturer.Cognex)]
        [InlineData(CameraManufacturer.Keyence)]
        [InlineData(CameraManufacturer.Baumer)]
        [InlineData(CameraManufacturer.Allied_Vision)]
        [InlineData(CameraManufacturer.FLIR)]
        [InlineData(CameraManufacturer.JAI)]
        public void NoSdkManufacturer_IsReportedAsSimulationFallback(CameraManufacturer manufacturer)
        {
            var creation = CameraAcquisitionFactory.CreateWithInfo(
                new CameraInfo { Manufacturer = manufacturer.ToString() });

            Assert.IsType<SimulatedCameraAcquisition>(creation.Acquisition);
            Assert.True(creation.IsSimulationFallback);
            Assert.Contains("SDK 미탑재", creation.FallbackReason);
            Assert.Contains("시뮬레이션", creation.FallbackReason);
        }

        [Theory]
        [InlineData("Allied Vision Technologies", "Allied Vision")]
        [InlineData("Teledyne FLIR", "FLIR")]
        [InlineData("IDS Imaging", "IDS")]
        public void NoSdkManufacturer_MatchesVendorStringVariants(string manufacturer, string expected)
            => Assert.Equal(expected, CameraAcquisitionFactory.FindNoSdkManufacturer(manufacturer));

        [Theory]
        [InlineData(CameraManufacturer.Virtual)]
        [InlineData(CameraManufacturer.Other)]
        public void VirtualOrOther_StaysIntentionalSimulation(CameraManufacturer manufacturer)
        {
            var creation = CameraAcquisitionFactory.CreateWithInfo(
                new CameraInfo { Manufacturer = manufacturer.ToString() });

            Assert.IsType<SimulatedCameraAcquisition>(creation.Acquisition);
            Assert.False(creation.IsSimulationFallback);
            Assert.Null(creation.FallbackReason);
        }
    }
}
