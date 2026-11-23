using System;
using VMS.Camera.Models;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 제조사 화면 표시 이름 — 저장 값(열거형 이름)의 밑줄을 화면에 그대로 내보내지 않고,
    /// 화면에서 고른 이름을 저장할 때는 저장 값으로 되돌린다(설정 파일 값은 그대로).
    /// </summary>
    public class CameraManufacturerNamesTests
    {
        [Theory]
        [InlineData("Mech_Mind", "Mech-Mind")]
        [InlineData("Mech-Mind", "Mech-Mind")]
        [InlineData("Allied_Vision", "Allied Vision")]
        [InlineData("Basler", "Basler")]
        [InlineData("Point Grey", "Point Grey")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void Display_name_has_no_underscore(string? stored, string expected)
        {
            Assert.Equal(expected, CameraManufacturerNames.ToDisplayName(stored));
        }

        [Fact]
        public void Every_manufacturer_round_trips_to_its_stored_name()
        {
            foreach (var value in Enum.GetValues<CameraManufacturer>())
            {
                var display = CameraManufacturerNames.ToDisplayName(value);
                Assert.DoesNotContain("_", display);
                Assert.Equal(value.ToString(), CameraManufacturerNames.ToStoredName(display));
            }
        }

        [Theory]
        [InlineData("Mech-Mind", "Mech_Mind")]
        [InlineData("mech mind", "Mech_Mind")]
        [InlineData("Allied Vision", "Allied_Vision")]
        [InlineData(" Basler ", "Basler")]
        [InlineData("Point Grey", "Point Grey")]   // 목록에 없는 이름은 입력 그대로
        [InlineData(null, "")]
        public void Stored_name_is_the_enum_name_when_known(string? display, string expected)
        {
            Assert.Equal(expected, CameraManufacturerNames.ToStoredName(display));
        }

        [Fact]
        public void Camera_info_display_uses_display_name_and_keeps_stored_value()
        {
            var camera = new CameraInfo { Manufacturer = "Mech_Mind", Model = "PRO S" };

            Assert.Equal("Mech-Mind", camera.ManufacturerDisplayName);
            Assert.Equal("Mech-Mind PRO S", camera.DisplayInfo);
            Assert.Equal("Mech_Mind", camera.Manufacturer);
        }
    }
}
