using System.Text.Json;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// `system_config.json` 의 키 표기가 두 가지다 — 설정 마법사는 camelCase 로 쓰고,
    /// 사람이 손으로 넣을 때는 런타임 형식 그대로 PascalCase 를 쓴다.
    /// 한쪽만 읽으면 "설정했는데 안 붙는다" 가 되고, 그건 화면에 아무 단서도 남기지 않는다.
    /// </summary>
    public class SystemConfigReaderTests
    {
        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

        [Fact]
        public void ReadString_FindsExactName()
            => Assert.Equal("http://a:5310",
                SystemConfigReader.ReadString(Parse("""{"MlopsServerUrl":"http://a:5310"}"""), "MlopsServerUrl"));

        [Fact]
        public void ReadString_FindsCamelCaseWhenAskedForPascalCase()
            => Assert.Equal("http://b:5310",
                SystemConfigReader.ReadString(Parse("""{"mlopsServerUrl":"http://b:5310"}"""), "MlopsServerUrl"));

        [Fact]
        public void ReadString_FindsPascalCaseWhenAskedForCamelCase()
            => Assert.Equal("http://c:5310",
                SystemConfigReader.ReadString(Parse("""{"MlopsServerUrl":"http://c:5310"}"""), "mlopsServerUrl"));

        [Fact]
        public void ReadString_ReturnsEmptyForMissingKey()
            => Assert.Equal("", SystemConfigReader.ReadString(Parse("""{"other":"x"}"""), "WebServerUrl"));

        /// <summary>값이 문자열이 아니면 없는 것으로 본다 — 잘못 넣은 설정이 예외로 앱을 세우면 안 된다.</summary>
        [Fact]
        public void ReadString_ReturnsEmptyForNonStringValue()
        {
            Assert.Equal("", SystemConfigReader.ReadString(Parse("""{"WebServerUrl":5310}"""), "WebServerUrl"));
            Assert.Equal("", SystemConfigReader.ReadString(Parse("""{"WebServerUrl":null}"""), "WebServerUrl"));
        }

        [Fact]
        public void ReadString_ReturnsEmptyWhenRootIsNotObject()
            => Assert.Equal("", SystemConfigReader.ReadString(Parse("""[1,2,3]"""), "WebServerUrl"));

        /// <summary>파일이 없는 PC 에서도 앱은 떠야 한다. 그때는 그 기능만 꺼진 채로 남는다.</summary>
        [Fact]
        public void ReadServerSettings_DoesNotThrowWhenFileMissing()
        {
            var settings = SystemConfigReader.ReadServerSettings();
            Assert.NotNull(settings.MlopsServerUrl);
            Assert.NotNull(settings.MlopsLineToken);
            Assert.NotNull(settings.WebServerUrl);
        }
    }
}
