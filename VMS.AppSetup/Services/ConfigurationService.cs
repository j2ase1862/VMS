using VMS.AppSetup.Interfaces;
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
    public class ConfigurationService : IConfigurationService
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
            : this(VMS.Camera.Configuration.AppDataPaths.Root)
        {
        }

        /// <summary>
        /// 임의 폴더로 별도 인스턴스를 만들 수 있는 테스트 친화 ctor.
        /// internal — VMS.AppSetup.Tests 전용. 운영 코드는 <see cref="Instance"/> 싱글톤 사용.
        /// </summary>
        internal ConfigurationService(string configFolderPath)
        {
            _configFolderPath = configFolderPath;
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
        /// 직전 <see cref="LoadConfiguration"/> 의 실패 원인. null 이면 성공 또는 파일 없음(정상).
        /// wizard 가 "기본값으로 시작" 사실을 운영자에게 알리는 데 사용 — 무통보로 기본값이
        /// 뜨면 저장 시 기존 설정이 통째로 덮어써지는 사고로 이어진다.
        /// </summary>
        public string? LastLoadError { get; private set; }

        /// <summary>파싱 실패한 원본을 보존하는 경로 — 진단/복구용.</summary>
        public string InvalidBackupPath => _configFilePath + ".invalid.bak";

        /// <summary>
        /// 설정 로드. 파일이 없으면 null (신규 install — 정상).
        /// 파싱 실패 시에도 null 을 반환하되 LastLoadError 에 원인을 남기고,
        /// 원본을 InvalidBackupPath 로 보존한다 (이후 저장이 원본을 덮어써도 복구 가능).
        /// </summary>
        public SetupConfiguration? LoadConfiguration()
        {
            LastLoadError = null;
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
                LastLoadError = ex.Message;
                try
                {
                    File.Copy(_configFilePath, InvalidBackupPath, overwrite: true);
                }
                catch (Exception backupEx)
                {
                    // 보존 실패는 치명적이지 않음 — 원인 메시지에 덧붙여 운영자가 수동 백업하도록.
                    LastLoadError += $" (원본 보존 실패: {backupEx.Message})";
                }
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
