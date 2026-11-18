using System;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VS-36 ④: [Connect] · 창 열기 자동 연결이 서버 주소만 확인해, 받지 않은 모델 이름이어도 '연결 완료' 가 되던 문제 —
    /// 서버의 모델 목록에 요청한 모델이 있어야 연결한다.
    /// </summary>
    public class SlmModelCheckTests
    {
        private const string TagsJson = """
            {"models":[
              {"name":"qwen2.5:7b-instruct","model":"qwen2.5:7b-instruct","size":4683087332},
              {"name":"llama3.2:latest","model":"llama3.2:latest","size":2019393189}
            ]}
            """;

        [Fact]
        public void ParseInstalledModels_ReadsNames()
        {
            var names = SLMChatService.ParseInstalledModels(TagsJson);

            Assert.Equal(new[] { "qwen2.5:7b-instruct", "llama3.2:latest" }, names);
        }

        [Fact]
        public void ParseInstalledModels_EmptyList_IsEmptyNotNull()
        {
            var names = SLMChatService.ParseInstalledModels("""{"models":[]}""");

            Assert.NotNull(names);
            Assert.Empty(names!);
        }

        [Theory]
        [InlineData("Ollama is running")]
        [InlineData("""{"error":"not found"}""")]
        [InlineData("""{"models":"x"}""")]
        [InlineData("[]")]
        public void ParseInstalledModels_UnreadableResponse_IsNull(string body)
        {
            Assert.Null(SLMChatService.ParseInstalledModels(body));
        }

        [Fact]
        public void ParseInstalledModels_SkipsEntriesWithoutName()
        {
            var names = SLMChatService.ParseInstalledModels(
                """{"models":[{"model":"x"},{"name":""},{"name":"a:1"},"bad"]}""");

            Assert.Equal(new[] { "a:1" }, names);
        }

        [Theory]
        [InlineData("qwen2.5:7b-instruct", "qwen2.5:7b-instruct")]
        [InlineData("  QWEN2.5:7B-Instruct ", "qwen2.5:7b-instruct")]
        [InlineData("llama3.2", "llama3.2:latest")]          // 태그 없이 적으면 :latest
        [InlineData("llama3.2:latest", "llama3.2:latest")]
        public void FindInstalledModel_MatchesInstalled(string requested, string expected)
        {
            var installed = SLMChatService.ParseInstalledModels(TagsJson)!;

            Assert.Equal(expected, SLMChatService.FindInstalledModel(installed, requested));
        }

        [Theory]
        [InlineData("qwen2.5")]               // :latest 는 받지 않았다
        [InlineData("qwen2.5:14b-instruct")]
        [InlineData("mistral")]
        [InlineData("   ")]
        public void FindInstalledModel_NotInstalled_IsNull(string requested)
        {
            var installed = SLMChatService.ParseInstalledModels(TagsJson)!;

            Assert.Null(SLMChatService.FindInstalledModel(installed, requested));
        }

        [Fact]
        public void BuildModelMissingMessage_ListsInstalledModels()
        {
            var msg = SLMChatService.BuildModelMissingMessage(" mistral ",
                SLMChatService.ParseInstalledModels(TagsJson)!);

            Assert.Contains("'mistral' 모델이 없습니다", msg);
            Assert.Contains("받아 둔 모델: qwen2.5:7b-instruct, llama3.2:latest", msg);
        }

        [Fact]
        public void BuildModelMissingMessage_NoModels_SaysSo()
        {
            var msg = SLMChatService.BuildModelMissingMessage("mistral", Array.Empty<string>());

            Assert.Contains("받아 둔 모델이 없습니다", msg);
        }

        [Fact]
        public void BuildModelMissingMessage_LongList_IsTruncated()
        {
            var many = new[] { "m1", "m2", "m3", "m4", "m5", "m6", "m7", "m8", "m9", "m10" };

            var msg = SLMChatService.BuildModelMissingMessage("x", many);

            Assert.Contains("m8 외 2개", msg);
            Assert.DoesNotContain("m9", msg);
        }
    }
}
