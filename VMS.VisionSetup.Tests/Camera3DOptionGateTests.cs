using System.Threading.Tasks;
using VMS.Camera.Models;
using VMS.Camera.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 3D 카메라 확장 옵션 — 옵션이 꺼지면 3D 카메라는 연결을 거절한다(시뮬레이션으로 대신 붙지 않음).
    /// 정적 스위치를 바꾸므로 같은 스위치를 쓰는 다른 테스트와 병렬 실행되지 않게 컬렉션으로 묶는다.
    /// </summary>
    [Collection(nameof(CameraFactoryStaticState))]
    public class Camera3DOptionGateTests
    {
        [Theory]
        [InlineData("Mech-Mind", CameraType.AreaScan3D)]
        [InlineData("Mech_Mind", CameraType.AreaScan2D)]
        [InlineData("Other", CameraType.LineScan3D)]
        public async Task Disabled_option_refuses_3d_cameras(string manufacturer, CameraType type)
        {
            CameraAcquisitionFactory.Allow3DCamera = false;
            try
            {
                var camera = new CameraInfo { Manufacturer = manufacturer, CameraType = type };
                var creation = CameraAcquisitionFactory.CreateWithInfo(camera);

                var disabled = Assert.IsType<OptionDisabledCameraAcquisition>(creation.Acquisition);
                Assert.False(creation.IsSimulationFallback);
                Assert.False(await disabled.ConnectAsync(camera));
                var result = await disabled.AcquireAsync();
                Assert.False(result.Success);
                Assert.Equal(CameraAcquisitionFactory.Camera3DDisabledReason, result.Message);
            }
            finally
            {
                CameraAcquisitionFactory.Allow3DCamera = true;
            }
        }

        [Fact]
        public void Disabled_option_leaves_2d_cameras_alone()
        {
            CameraAcquisitionFactory.Allow3DCamera = false;
            try
            {
                var creation = CameraAcquisitionFactory.CreateWithInfo(
                    new CameraInfo { Manufacturer = "Basler", CameraType = CameraType.AreaScan2D });

                Assert.IsNotType<OptionDisabledCameraAcquisition>(creation.Acquisition);
            }
            finally
            {
                CameraAcquisitionFactory.Allow3DCamera = true;
            }
        }

        [Fact]
        public void Enabled_option_is_the_default()
        {
            Assert.True(CameraAcquisitionFactory.Allow3DCamera);
            var creation = CameraAcquisitionFactory.CreateWithInfo(new CameraInfo { Manufacturer = "Mech-Mind" });
            Assert.IsNotType<OptionDisabledCameraAcquisition>(creation.Acquisition);
        }
    }

    [CollectionDefinition(nameof(CameraFactoryStaticState), DisableParallelization = true)]
    public class CameraFactoryStaticState { }
}
