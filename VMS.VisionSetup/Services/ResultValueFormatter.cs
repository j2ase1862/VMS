using System;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 도구 결과(Data) 값을 Run Results 표에 보일 글자로 바꾼다 — VisionSetup 과 VMS 메인의
    /// Run Results 가 같이 쓰는 단일 정의처.
    ///
    /// <para>실수는 소수 3자리. 목록은 앞의 몇 개만 [a, b, …] 로 보이고 전체 개수를 붙인다.
    /// 글자로 바꿀 방법이 없는 값(ToString 이 형식 이름을 돌려주는 객체)은 형식 이름 대신
    /// 개수만 보인다 — 예: 검출 목록 "1개".</para>
    /// </summary>
    public static class ResultValueFormatter
    {
        /// <summary>목록에서 글자로 펼쳐 보일 최대 항목 수.</summary>
        public const int MaxListItems = 4;

        public static string Format(object? value) => value switch
        {
            null => "",
            string s => s,
            double d => d.ToString("F3"),
            float f => f.ToString("F3"),
            decimal m => m.ToString("F3"),
            IDictionary dict => $"{dict.Count}개",
            IEnumerable list => FormatList(list),
            ITuple tuple => FormatTuple(tuple),
            _ => FormatObject(value)
        };

        private static string FormatList(IEnumerable list)
        {
            var items = list.Cast<object?>().ToList();
            if (items.Count == 0) return "0개";

            // 항목 하나라도 글자로 못 바꾸면 개수만 — 형식 이름이 줄줄이 찍히는 것을 막는다.
            var texts = items.Take(MaxListItems).Select(FormatItem).ToList();
            if (texts.Any(t => t == null)) return $"{items.Count}개";

            string joined = string.Join(", ", texts);
            return items.Count > MaxListItems
                ? $"[{joined}, …] ({items.Count}개)"
                : $"[{joined}]";
        }

        private static string FormatTuple(ITuple tuple)
        {
            var parts = new string[tuple.Length];
            for (int i = 0; i < tuple.Length; i++)
                parts[i] = Format(tuple[i]);
            return $"({string.Join(", ", parts)})";
        }

        /// <summary>목록 항목 — 글자로 바꿀 수 없으면 null.</summary>
        private static string? FormatItem(object? item) => item switch
        {
            null => "",
            IEnumerable and not string => null,
            _ when HasOwnText(item) || item is ITuple || item is IFormattable => Format(item),
            _ => null
        };

        private static string FormatObject(object value)
        {
            if (HasOwnText(value)) return value.ToString() ?? "";
            return value.GetType().Name;
        }

        /// <summary>ToString 이 형식 이름이 아닌 자기 글자를 돌려주는지.</summary>
        private static bool HasOwnText(object value)
        {
            var text = value.ToString();
            var type = value.GetType();
            return !string.IsNullOrEmpty(text) && text != type.FullName && text != type.ToString();
        }
    }
}
