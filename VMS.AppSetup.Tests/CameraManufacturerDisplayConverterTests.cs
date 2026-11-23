using System;
using System.Globalization;
using System.Windows.Data;
using VMS.AppSetup.Converters;
using VMS.Camera.Models;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// 설정 마법사 카메라 제조사 콤보의 표시 이름 — VisionSetup Camera Manager 와 같은 이름을 보이고,
    /// 저장 값(열거형)은 건드리지 않는다.
    /// </summary>
    public class CameraManufacturerDisplayConverterTests
    {
        private readonly CameraManufacturerDisplayConverter _converter = new();

        [Theory]
        [InlineData(CameraManufacturer.Mech_Mind, "Mech-Mind")]
        [InlineData(CameraManufacturer.Allied_Vision, "Allied Vision")]
        [InlineData(CameraManufacturer.Basler, "Basler")]
        [InlineData(CameraManufacturer.HIK, "HIK")]
        [InlineData(CameraManufacturer.Other, "Other")]
        public void Shows_display_name_without_underscore(CameraManufacturer value, string expected)
        {
            var text = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);
            Assert.Equal(expected, text);
        }

        [Fact]
        public void Every_enum_value_matches_visionsetup_display_name_and_round_trips()
        {
            foreach (var value in Enum.GetValues<CameraManufacturer>())
            {
                var text = (string)_converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);
                Assert.DoesNotContain("_", text);
                Assert.Equal(CameraManufacturerNames.ToDisplayName(value), text);
                // 표시 이름을 저장 값으로 되돌리면 열거형 이름 그대로 — 설정 파일 값은 바뀌지 않는다
                Assert.Equal(value.ToString(), CameraManufacturerNames.ToStoredName(text));
            }
        }

        [Fact]
        public void Does_not_write_back()
        {
            var back = _converter.ConvertBack("Mech-Mind", typeof(CameraManufacturer), null!, CultureInfo.InvariantCulture);
            Assert.Same(Binding.DoNothing, back);
        }
    }
}
