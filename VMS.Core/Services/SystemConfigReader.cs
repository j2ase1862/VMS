using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VMS.Camera.Configuration;

namespace VMS.Core.Services
{
    /// <summary>
    /// `system_config.json` 에서 서버 연동에 필요한 값만 읽는다.
    ///
    /// <para>
    /// VisionSetup 과 AI 학습 도구는 VMS 런타임의 <c>SystemConfiguration</c> 형식을 직접 참조하지 않는다.
    /// 그래서 필요한 키만 직접 읽는데, 그 읽는 방법이 앱마다 갈리면 "설정했는데 안 붙는다" 가 생긴다.
    /// 한 곳에 모아 둔다.
    /// </para>
    /// <para>
    /// 키 표기가 둘인 것에 주의한다. 설정 마법사(AppSetup)는 <c>JsonNamingPolicy.CamelCase</c> 로 쓰고,
    /// 사람이 손으로 넣을 때는 런타임 형식 그대로 PascalCase 를 쓴다. 둘 다 읽는다.
    /// </para>
    /// </summary>
    public static class SystemConfigReader
    {
        public static string ConfigPath => AppDataPaths.SystemConfigFile;

        /// <summary>서버 연동에 쓰는 값들. 없으면 빈 문자열이고, 그때는 그 기능이 꺼진 상태로 남는다.</summary>
        public readonly record struct ServerSettings(
            string MlopsServerUrl,
            string MlopsLineToken,
            string WebServerUrl);

        public static ServerSettings ReadServerSettings()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new ServerSettings("", "", "");
                using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                var root = document.RootElement;
                return new ServerSettings(
                    ReadString(root, "MlopsServerUrl"),
                    ReadString(root, "MlopsLineToken"),
                    ReadString(root, "WebServerUrl"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemConfig] 읽지 못했습니다: {ex.Message}");
                return new ServerSettings("", "", "");
            }
        }

        /// <summary>
        /// 대소문자를 가리지 않고 찾는다. AppSetup 은 camelCase 로 쓰고 사람이 손으로 넣은 파일은
        /// PascalCase 일 수 있는데, 둘을 갈라 다루면 "설정했는데 안 붙는다" 가 된다.
        /// </summary>
        public static string ReadString(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object) return "";
            foreach (var property in root.EnumerateObject())
            {
                if (!property.NameEquals(name)
                    && !string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : "";
            }
            return "";
        }
    }
}
