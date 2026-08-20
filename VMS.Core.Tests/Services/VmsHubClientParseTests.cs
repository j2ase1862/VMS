using System.Text.Json;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// RecipeParametersChanged 페이로드 파싱 — Web 은 camelCase({"recipeId":N})로
    /// 브로드캐스트하지만 직렬화 설정 변화에 대비해 PascalCase 도 허용.
    /// </summary>
    public class VmsHubClientParseTests
    {
        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

        [Theory]
        [InlineData("{\"recipeId\": 7}", 7)]
        [InlineData("{\"RecipeId\": 12}", 12)]
        public void TryParseRecipeId_ValidPayload_ReturnsId(string json, int expected)
        {
            Assert.Equal(expected, VmsHubClient.TryParseRecipeId(Parse(json)));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"recipeId\": \"abc\"}")]
        [InlineData("[1,2]")]
        [InlineData("5")]
        public void TryParseRecipeId_InvalidPayload_ReturnsNull(string json)
        {
            Assert.Null(VmsHubClient.TryParseRecipeId(Parse(json)));
        }

        // LotIssued/LotClosed 페이로드 — {"workOrderId":N, "lotId":N, "lotNumber":"..."}

        [Theory]
        [InlineData("{\"workOrderId\": 3, \"lotId\": 9, \"lotNumber\": \"20260820-WO-001-002\"}", 3)]
        [InlineData("{\"WorkOrderId\": 15}", 15)]
        public void TryParseWorkOrderId_ValidPayload_ReturnsId(string json, int expected)
        {
            Assert.Equal(expected, VmsHubClient.TryParseWorkOrderId(Parse(json)));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"workOrderId\": \"abc\"}")]
        [InlineData("[3]")]
        public void TryParseWorkOrderId_InvalidPayload_ReturnsNull(string json)
        {
            Assert.Null(VmsHubClient.TryParseWorkOrderId(Parse(json)));
        }
    }
}
