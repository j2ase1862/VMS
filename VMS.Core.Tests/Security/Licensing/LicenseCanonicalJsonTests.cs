using System.Text;
using System.Text.Json.Nodes;
using VMS.Core.Security.Licensing;
using Xunit;

namespace VMS.Core.Tests.Security.Licensing
{
    /// <summary>
    /// LicenseCanonicalJson — 키 순서 무관 동일 바이트 보장.
    /// VMS/Web/LicGen 3자가 공유하는 서명 입력의 결정성이 전체 체계의 전제 (spec §3).
    /// </summary>
    public class LicenseCanonicalJsonTests
    {
        [Fact]
        public void Key_order_does_not_change_output()
        {
            var a = JsonNode.Parse("{\"b\":1,\"a\":\"x\",\"c\":{\"z\":true,\"y\":null}}")!;
            var b = JsonNode.Parse("{\"c\":{\"y\":null,\"z\":true},\"a\":\"x\",\"b\":1}")!;

            Assert.Equal(LicenseCanonicalJson.Serialize(a), LicenseCanonicalJson.Serialize(b));
        }

        [Fact]
        public void Output_is_compact_and_sorted()
        {
            var node = JsonNode.Parse("{ \"b\" : [1, 2],  \"a\" : \"한글\" }")!;
            var text = Encoding.UTF8.GetString(LicenseCanonicalJson.Serialize(node));

            Assert.Equal("{\"a\":\"\\uD55C\\uAE00\",\"b\":[1,2]}", text);
        }

        [Fact]
        public void Array_order_is_preserved()
        {
            var a = JsonNode.Parse("{\"features\":[\"b\",\"a\"]}")!;
            var b = JsonNode.Parse("{\"features\":[\"a\",\"b\"]}")!;

            Assert.NotEqual(LicenseCanonicalJson.Serialize(a), LicenseCanonicalJson.Serialize(b));
        }

        [Fact]
        public void Source_node_is_not_mutated()
        {
            var node = JsonNode.Parse("{\"b\":1,\"a\":2}")!;
            LicenseCanonicalJson.Serialize(node);

            Assert.Equal("{\"b\":1,\"a\":2}", node.ToJsonString());
        }
    }
}
