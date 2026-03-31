using System;
using System.Diagnostics;
using System.Reflection;
using VMS.Core.Interfaces;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// VisionToolBase의 LinkedParamCodes를 읽어 IParameterSyncService 캐시에서
    /// 값을 resolve한 뒤 도구 프로퍼티에 적용하는 서비스.
    /// BODA의 ParameterSyncToToolService.ApplyWebParameters() 패턴.
    /// </summary>
    public class ParameterApplyService : IParameterApplyService
    {
        private readonly IParameterSyncService _syncService;

        public ParameterApplyService(IParameterSyncService syncService)
        {
            _syncService = syncService;
        }

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

                if (TrySetProperty(tool, propertyName, value))
                {
                    appliedCount++;
                    Debug.WriteLine($"[ParameterApply] Applied {tool.Name}.{propertyName} = {value} (ParamCode={paramCode})");
                }
                else
                {
                    Debug.WriteLine($"[ParameterApply] Failed to set {tool.Name}.{propertyName} (ParamCode={paramCode})");
                }
            }

            return appliedCount;
        }

        private static bool TrySetProperty(VisionToolBase tool, string propertyName, double value)
        {
            try
            {
                var prop = tool.GetType().GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop == null || !prop.CanWrite)
                    return false;

                var targetType = prop.PropertyType;

                object convertedValue;
                if (targetType == typeof(double))
                    convertedValue = value;
                else if (targetType == typeof(float))
                    convertedValue = (float)value;
                else if (targetType == typeof(int))
                    convertedValue = (int)Math.Round(value);
                else if (targetType == typeof(long))
                    convertedValue = (long)Math.Round(value);
                else if (targetType == typeof(bool))
                    convertedValue = value != 0.0;
                else
                    convertedValue = Convert.ChangeType(value, targetType);

                prop.SetValue(tool, convertedValue);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParameterApply] SetProperty error: {ex.Message}");
                return false;
            }
        }
    }
}
