using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// 서명 대상 payload 의 정규화 직렬화 — 키 알파벳순 정렬 + 공백 없음.
    ///
    /// VMS / BODA.VMS.Web / LicGen 이 반드시 같은 정규화를 써야 한다 (spec §3) —
    /// 직렬화 옵션 차이로 현장 검증이 깨지는 사고 방지가 이 클래스의 존재 이유.
    /// JsonNode 를 그대로 정렬·재기록하므로 검증기가 모르는 필드가 있어도 서명이 유지된다
    /// (전방 호환 — 새 필드 추가 시 구버전 검증기에서도 서명 유효).
    /// </summary>
    public static class LicenseCanonicalJson
    {
        /// <summary>payload 노드를 canonical UTF-8 바이트로 직렬화 (서명/검증 공용 입력).</summary>
        public static byte[] Serialize(JsonNode payload)
        {
            var canonical = Canonicalize(payload);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))  // 기본 옵션 = 들여쓰기 없음
            {
                canonical.WriteTo(writer);
            }
            return ms.ToArray();
        }

        /// <summary>객체 키를 서수(ordinal) 알파벳순으로 재귀 정렬한 사본 반환.</summary>
        private static JsonNode Canonicalize(JsonNode node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var sorted = new JsonObject();
                    foreach (var kv in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                        sorted[kv.Key] = kv.Value == null ? null : Canonicalize(kv.Value);
                    return sorted;
                case JsonArray arr:
                    var copy = new JsonArray();
                    foreach (var item in arr)
                        copy.Add(item == null ? null : Canonicalize(item));
                    return copy;
                default:
                    // 값 노드 — 사본으로 복제 (JsonNode 는 부모 재부착 불가)
                    return JsonNode.Parse(node.ToJsonString())!;
            }
        }
    }
}
