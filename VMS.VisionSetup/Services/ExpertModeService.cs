using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 도구 타입(ToolType) 별 Expert 모드 ON/OFF 플래그를 영구 저장하는 싱글톤.
    ///
    /// Rationale: 사용자가 룰베이스 도구는 잘 알아 Expert 로 쓰고, 딥러닝 도구는 익숙하지 않아
    /// Basic 으로 쓰고 싶다는 시나리오를 지원. 전역 단일 토글 대신 도구 타입별 독립 토글.
    ///
    /// 저장 위치: %LocalAppData%/BODA VISION AI/expert_mode.json — system_config.json 과 분리해
    /// AppSetup 마법사의 write 와 충돌 없게.
    /// </summary>
    public class ExpertModeService
    {
        private static readonly Lazy<ExpertModeService> _instance = new(() => new ExpertModeService());
        public static ExpertModeService Instance => _instance.Value;

        private readonly string _configPath;
        private readonly ConcurrentDictionary<string, bool> _expertByType = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>toolType 인자와 함께 발생 — 같은 ToolType 의 다른 인스턴스 ViewModel 들이 즉시 UI 갱신.</summary>
        public event EventHandler<string>? ExpertModeChanged;

        private ExpertModeService()
        {
            _configPath = VMS.Camera.Configuration.AppDataPaths.GetPath("expert_mode.json");
            Load();
        }

        public bool IsExpert(string? toolType)
        {
            if (string.IsNullOrWhiteSpace(toolType)) return false;
            return _expertByType.TryGetValue(toolType, out var v) && v;
        }

        public void SetExpert(string? toolType, bool value)
        {
            if (string.IsNullOrWhiteSpace(toolType)) return;
            // 기존 값과 동일하면 저장/이벤트 skip — 불필요한 디스크 IO/UI 갱신 회피.
            if (_expertByType.TryGetValue(toolType, out var existing) && existing == value) return;

            _expertByType[toolType] = value;
            Save();
            ExpertModeChanged?.Invoke(this, toolType);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_configPath)) return;
                var json = File.ReadAllText(_configPath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, JsonOptions);
                if (dict == null) return;
                foreach (var kvp in dict)
                    _expertByType[kvp.Key] = kvp.Value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExpertModeService] Load error: {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(new Dictionary<string, bool>(_expertByType), JsonOptions);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExpertModeService] Save error: {ex.Message}");
            }
        }
    }
}
