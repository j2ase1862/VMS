using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services.BatchTesting
{
    public enum TunableKind { Numeric, Bool, Enum }

    /// <summary>도구의 튜닝 가능한 파라미터 — Reflection으로 자동 발견.</summary>
    public class TunableParameterDescriptor
    {
        public PropertyInfo Property { get; init; } = null!;
        public string Name => Property.Name;
        public Type Type => Property.PropertyType;
        public TunableKind Kind { get; init; }
        public object? CurrentValue { get; set; }
        public List<object>? EnumValues { get; init; }

        public override string ToString() => $"{Name} ({Kind})";

        public object? GetValue(VisionToolBase tool) => Property.GetValue(tool);

        public void SetValue(VisionToolBase tool, object? value)
        {
            if (value == null) return;
            try
            {
                // 숫자형은 변환 필요 (double → int 등)
                var coerced = Convert.ChangeType(value, Type, CultureInfo.InvariantCulture);
                Property.SetValue(tool, coerced);
            }
            catch
            {
                Property.SetValue(tool, value);
            }
        }

        /// <summary>현재 값을 사람이 읽는 문자열로.</summary>
        public string FormatValue(object? v) => v switch
        {
            null => "(null)",
            double d => d.ToString("G6", CultureInfo.InvariantCulture),
            float f => f.ToString("G6", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => v.ToString() ?? ""
        };
    }

    /// <summary>
    /// 도구 인스턴스에서 튜닝 가능한 파라미터를 Reflection으로 발견.
    /// VisionToolBase 의 공통 속성(Id, Name, ROI 등)은 제외 — 도구 specific 속성만.
    /// </summary>
    public static class TunableParameterDiscovery
    {
        // VisionToolBase / ObservableObject에서 상속받은 노이즈 속성 제외
        private static readonly HashSet<string> ExcludedNames = new()
        {
            "Id", "Name", "ToolType", "Sequence", "IsEnabled", "Description",
            "ROI", "Roi", "InputROI", "SearchROI", "TrainingROI",
            "LastResult", "InputImageId", "OutputImageId",
            "Category", "Icon", "IsExpanded", "IsSelected"
        };

        public static List<TunableParameterDescriptor> Discover(VisionToolBase tool)
        {
            var result = new List<TunableParameterDescriptor>();
            var props = tool.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in props)
            {
                if (!p.CanRead || !p.CanWrite) continue;
                if (ExcludedNames.Contains(p.Name)) continue;
                if (p.GetIndexParameters().Length > 0) continue;  // indexer 제외

                var t = p.PropertyType;
                TunableKind kind;
                List<object>? enumVals = null;

                if (t == typeof(int) || t == typeof(double) || t == typeof(float) ||
                    t == typeof(long) || t == typeof(short) || t == typeof(byte))
                {
                    kind = TunableKind.Numeric;
                }
                else if (t == typeof(bool))
                {
                    kind = TunableKind.Bool;
                }
                else if (t.IsEnum)
                {
                    kind = TunableKind.Enum;
                    enumVals = Enum.GetValues(t).Cast<object>().ToList();
                }
                else
                {
                    continue;
                }

                object? cur = null;
                try { cur = p.GetValue(tool); } catch { /* skip */ }

                result.Add(new TunableParameterDescriptor
                {
                    Property = p,
                    Kind = kind,
                    CurrentValue = cur,
                    EnumValues = enumVals
                });
            }

            // 이름 정렬 — 사용자 친화적
            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }
    }
}
