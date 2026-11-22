using System;

namespace VMS.Camera.Models
{
    /// <summary>
    /// 카메라 제조사의 화면 표시 이름.
    /// 저장 값(<see cref="CameraInfo.Manufacturer"/> · system_config.json)은 <see cref="CameraManufacturer"/>
    /// 열거형 이름 그대로("Mech_Mind", "Allied_Vision")라 밑줄이 들어 있다 — 화면에는 밑줄 없는 이름을 보여 준다.
    /// 저장 값 자체는 바꾸지 않는다.
    /// </summary>
    public static class CameraManufacturerNames
    {
        /// <summary>저장 값 → 화면 표시 이름. 모르는 값은 밑줄만 공백으로 바꾼다.</summary>
        public static string ToDisplayName(string? manufacturer)
        {
            var value = (manufacturer ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;
            if (Normalize(value).Equals("MechMind", StringComparison.OrdinalIgnoreCase)) return "Mech-Mind";
            return value.Replace('_', ' ');
        }

        /// <summary>열거형 값 → 화면 표시 이름.</summary>
        public static string ToDisplayName(CameraManufacturer manufacturer) => ToDisplayName(manufacturer.ToString());

        /// <summary>
        /// 화면에서 입력 · 선택한 이름 → 저장 값. 표시 이름("Mech-Mind", "Allied Vision")이
        /// 열거형 항목과 같은 제조사면 열거형 이름으로 되돌리고, 목록에 없는 이름은 입력 그대로 둔다.
        /// </summary>
        public static string ToStoredName(string? displayName)
        {
            var value = (displayName ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;
            var normalized = Normalize(value);
            foreach (var name in Enum.GetNames<CameraManufacturer>())
            {
                if (Normalize(name).Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return value;
        }

        private static string Normalize(string value) =>
            value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
    }
}
