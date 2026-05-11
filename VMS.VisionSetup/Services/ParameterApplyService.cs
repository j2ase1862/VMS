using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using VMS.Core.Interfaces;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 외부 소스(Web 파라미터 동기화, SLM 추정 등)에서 받은 값을
    /// 리플렉션으로 비전 도구 프로퍼티에 적용하는 서비스.
    /// 기존 BODA의 ParameterSyncToToolService.ApplyWebParameters() 패턴을 일반화.
    /// </summary>
    public class ParameterApplyService : IParameterApplyService
    {
        private readonly IParameterSyncService _syncService;

        public ParameterApplyService(IParameterSyncService syncService)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Web 동기화 경로: LinkedParamCodes를 resolve해서 적용.
        /// </summary>
        public int ApplyParameters(VisionToolBase tool)
        {
            if (tool.LinkedParamCodes == null || tool.LinkedParamCodes.Count == 0)
                return 0;

            int appliedCount = 0;

            foreach (var (propertyName, paramCode) in tool.LinkedParamCodes)
            {
                if (!_syncService.ValidateCode(paramCode))
                {
                    Debug.WriteLine($"[ParameterApply] ParamCode {paramCode} not found in cache for {tool.Name}.{propertyName}");
                    continue;
                }

                var value = _syncService.ResolveValue(paramCode);

                if (TrySetNumeric(tool, propertyName, value, out var error))
                {
                    appliedCount++;
                    Debug.WriteLine($"[ParameterApply] Applied {tool.Name}.{propertyName} = {value} (ParamCode={paramCode})");
                }
                else
                {
                    Debug.WriteLine($"[ParameterApply] Failed to set {tool.Name}.{propertyName} (ParamCode={paramCode}): {error}");
                }
            }

            return appliedCount;
        }

        /// <summary>
        /// 외부 dictionary 경로: enum, bool, 숫자, 문자열을 자동 변환해 적용.
        /// </summary>
        public (int applied, List<string> warnings) ApplyParameters(
            VisionToolBase tool, IReadOnlyDictionary<string, object?> parameters)
        {
            var warnings = new List<string>();
            int appliedCount = 0;

            if (parameters == null || parameters.Count == 0)
                return (0, warnings);

            foreach (var (propertyName, rawValue) in parameters)
            {
                var prop = tool.GetType().GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop == null || !prop.CanWrite)
                {
                    warnings.Add($"{tool.Name}.{propertyName}: property not found or not writable");
                    continue;
                }

                if (!TryConvert(rawValue, prop.PropertyType, out var converted, out var convError))
                {
                    warnings.Add($"{tool.Name}.{propertyName}: {convError}");
                    continue;
                }

                try
                {
                    prop.SetValue(tool, converted);
                    appliedCount++;
                    Debug.WriteLine($"[ParameterApply] Set {tool.Name}.{propertyName} = {converted}");
                }
                catch (Exception ex)
                {
                    warnings.Add($"{tool.Name}.{propertyName}: setter threw — {ex.Message}");
                }
            }

            return (appliedCount, warnings);
        }

        /// <summary>
        /// Web 동기화 경로용 숫자 전용 변환(기존 동작 보존).
        /// </summary>
        private static bool TrySetNumeric(VisionToolBase tool, string propertyName, double value, out string error)
        {
            error = string.Empty;
            try
            {
                var prop = tool.GetType().GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop == null || !prop.CanWrite)
                {
                    error = "property not found";
                    return false;
                }

                var targetType = prop.PropertyType;
                object convertedValue;
                if (targetType == typeof(double)) convertedValue = value;
                else if (targetType == typeof(float)) convertedValue = (float)value;
                else if (targetType == typeof(int)) convertedValue = (int)Math.Round(value);
                else if (targetType == typeof(long)) convertedValue = (long)Math.Round(value);
                else if (targetType == typeof(bool)) convertedValue = value != 0.0;
                else convertedValue = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);

                prop.SetValue(tool, convertedValue);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// raw 값을 target 타입으로 변환. JsonElement, string, primitive 모두 처리.
        /// </summary>
        private static bool TryConvert(object? raw, Type targetType, out object? result, out string error)
        {
            result = null;
            error = string.Empty;

            if (raw == null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                {
                    result = null;
                    return true;
                }
                error = "null cannot be assigned to non-nullable value type";
                return false;
            }

            // JsonElement 평탄화
            if (raw is JsonElement je)
            {
                if (!TryFlattenJsonElement(je, out raw, out error))
                    return false;
            }

            var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try
            {
                // enum: 문자열 이름 또는 정수 모두 허용
                if (nonNullable.IsEnum)
                {
                    if (raw is string enumStr)
                    {
                        result = Enum.Parse(nonNullable, enumStr, ignoreCase: true);
                        return true;
                    }

                    var underlying = Enum.GetUnderlyingType(nonNullable);
                    var asNumber = Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
                    if (asNumber == null)
                    {
                        error = "enum numeric conversion produced null";
                        return false;
                    }
                    result = Enum.ToObject(nonNullable, asNumber);
                    return true;
                }

                // bool: "true"/"false" 문자열 또는 숫자도 허용
                if (nonNullable == typeof(bool))
                {
                    result = raw switch
                    {
                        bool b => b,
                        string s => bool.Parse(s),
                        _ => Convert.ToDouble(raw, CultureInfo.InvariantCulture) != 0.0
                    };
                    return true;
                }

                // string
                if (nonNullable == typeof(string))
                {
                    result = raw?.ToString() ?? string.Empty;
                    return true;
                }

                // 숫자 계열
                if (nonNullable == typeof(double))
                {
                    result = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    return true;
                }
                if (nonNullable == typeof(float))
                {
                    result = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
                    return true;
                }
                if (nonNullable == typeof(int))
                {
                    result = (int)Math.Round(Convert.ToDouble(raw, CultureInfo.InvariantCulture));
                    return true;
                }
                if (nonNullable == typeof(long))
                {
                    result = (long)Math.Round(Convert.ToDouble(raw, CultureInfo.InvariantCulture));
                    return true;
                }

                // 폴백
                result = Convert.ChangeType(raw, nonNullable, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = $"conversion to {targetType.Name} failed — {ex.Message}";
                return false;
            }
        }

        private static bool TryFlattenJsonElement(JsonElement je, out object? flattened, out string error)
        {
            error = string.Empty;
            switch (je.ValueKind)
            {
                case JsonValueKind.String:
                    flattened = je.GetString();
                    return true;
                case JsonValueKind.Number:
                    if (je.TryGetInt64(out var l)) { flattened = l; return true; }
                    flattened = je.GetDouble();
                    return true;
                case JsonValueKind.True:
                    flattened = true;
                    return true;
                case JsonValueKind.False:
                    flattened = false;
                    return true;
                case JsonValueKind.Null:
                    flattened = null;
                    return true;
                default:
                    flattened = null;
                    error = $"unsupported JsonElement kind: {je.ValueKind}";
                    return false;
            }
        }
    }
}
