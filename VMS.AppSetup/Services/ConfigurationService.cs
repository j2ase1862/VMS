using VMS.AppSetup.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.AppSetup.Services
{
    /// <summary>
    /// 설정 파일 관리 서비스
    /// </summary>
    public class ConfigurationService
    {
        private static readonly Lazy<ConfigurationService> _instance = new(() => new ConfigurationService());
        public static ConfigurationService Instance => _instance.Value;

        private readonly string _configFolderPath;
        private readonly string _configFilePath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        private ConfigurationService()
        {
            _configFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            _configFilePath = Path.Combine(_configFolderPath, "system_config.json");
            EnsureDirectoryExists();
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_configFolderPath))
            {
                Directory.CreateDirectory(_configFolderPath);
            }
        }

        /// <summary>
        /// 설정 파일 경로
        /// </summary>
        public string ConfigFilePath => _configFilePath;

        /// <summary>
        /// 설정 저장
        /// </summary>
        public bool SaveConfiguration(SetupConfiguration config)
        {
            try
            {
                EnsureDirectoryExists();
                config.CreatedAt = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_configFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 저장 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 설정 로드
        /// </summary>
        public SetupConfiguration? LoadConfiguration()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                    return null;

                var json = File.ReadAllText(_configFilePath);
                return JsonSerializer.Deserialize<SetupConfiguration>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 로드 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 설정 파일 존재 여부
        /// </summary>
        public bool ConfigurationExists()
        {
            return File.Exists(_configFilePath);
        }

        /// <summary>
        /// 특정 경로에 설정 내보내기
        /// </summary>
        public bool ExportConfiguration(SetupConfiguration config, string exportPath)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(exportPath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 내보내기 실패: {ex.Message}");
                return false;
            }
        }
    }
}
