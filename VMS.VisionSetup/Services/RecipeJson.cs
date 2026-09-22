using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 레시피 JSON 직렬화 옵션의 단일 정의처.
    ///
    /// <para>VMS(검사 실행)와 VisionSetup(편집)이 같은 파일을 읽고 쓰는데 옵션이 서로 달랐다.
    /// VMS 는 <see cref="JsonStringEnumConverter"/> 를 달고 있어 enum 을 문자열로 썼고,
    /// VisionSetup 은 컨버터가 없어 그 문자열을 읽다 예외를 내 레시피가 통째로 열리지
    /// 않았다 (LoadRecipe → null). 옵션을 한 곳에 두고 양쪽이 이것만 쓴다.</para>
    ///
    /// <para>enum 은 <b>숫자로 쓰고 문자열·숫자를 모두 읽는다</b>. 숫자로 쓰는 이유는
    /// 구버전 VisionSetup 이 설치된 PC 가 새로 저장된 레시피를 계속 열 수 있어야 하기
    /// 때문이고, 문자열도 읽는 이유는 과거 VMS 가 저장해 깨져 있던 레시피를 되살리기
    /// 위해서다. 속성에 <c>[JsonConverter]</c> 가 직접 붙은 필드(InspectionStep.
    /// PointCloudPostProcess 등)는 속성 쪽이 우선이므로 종전 표기를 유지한다.</para>
    ///
    /// <para>주의: 이 옵션은 <b>레시피 전용</b>이다. system_config.json 등 다른 설정
    /// 파일은 각자의 옵션을 그대로 쓴다 (그쪽은 문자열 enum 이 이미 규약).</para>
    /// </summary>
    public static class RecipeJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new TolerantEnumConverterFactory() }
        };
    }

    /// <summary>
    /// enum 읽기 관용 컨버터. 읽기는 문자열/숫자 모두 허용, 쓰기는 숫자.
    /// <c>Nullable&lt;TEnum&gt;</c> 은 System.Text.Json 의 내장 Nullable 처리가
    /// 밑바탕 타입의 컨버터(=이것)를 찾아 쓰므로 여기서 다루지 않는다.
    /// </summary>
    public sealed class TolerantEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(
                typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert))!;

        private sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
        {
            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        var text = reader.GetString();
                        if (Enum.TryParse<T>(text, ignoreCase: true, out var parsed))
                            return parsed;
                        throw new JsonException($"'{text}' 은(는) {typeof(T).Name} 의 값이 아닙니다.");

                    case JsonTokenType.Number:
                        if (reader.TryGetInt64(out var number))
                            return (T)Enum.ToObject(typeof(T), number);
                        break;
                }

                throw new JsonException(
                    $"{typeof(T).Name} 값으로 올바르지 않은 토큰입니다: {reader.TokenType}");
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
                => writer.WriteNumberValue(Convert.ToInt64(value));
        }
    }
}
