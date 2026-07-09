using VMS.Core.Security;
using VMS.Interfaces;
using VMS.Models;
using VMS.PLC.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.Services
{
    /// <summary>
    /// Service for loading system configuration from BODA.Setup
    /// </summary>
    public class ConfigurationService : IConfigurationService
    {
        private static ConfigurationService? _instance;
        public static ConfigurationService Instance => _instance ??= new ConfigurationService();

        private readonly string _configDirectory;
        private readonly string _systemConfigPath;
        private readonly string _layoutConfigPath;
        private readonly string _plcSignalConfigPath;

        // Must match BODA.Setup's JsonSerializerOptions for compatibility
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string ConfigDirectory => _configDirectory;

        private ConfigurationService()
            : this(VMS.Camera.Configuration.AppDataPaths.Root)
        {
        }

        /// <summary>
        /// 임의 디렉토리로 별도 인스턴스를 만들 수 있는 테스트 친화 ctor.
        /// internal — VMS.Tests 통합 테스트에서만 호출. 운영 코드는
        /// 반드시 <see cref="Instance"/> 싱글톤 사용.
        /// </summary>
        internal ConfigurationService(string configDirectory)
        {
            _configDirectory = configDirectory;
            _systemConfigPath = Path.Combine(_configDirectory, "system_config.json");
            _layoutConfigPath = Path.Combine(_configDirectory, "layout_config.json");
            _plcSignalConfigPath = Path.Combine(_configDirectory, "plc_signals.json");

            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }
        }

        /// <summary>
        /// Load system configuration (from BODA.Setup)
        /// </summary>
        public SystemConfiguration LoadSystemConfiguration()
        {
            try
            {
                if (File.Exists(_systemConfigPath))
                {
                    var json = File.ReadAllText(_systemConfigPath);
                    var config = JsonSerializer.Deserialize<SystemConfiguration>(json, JsonOptions);
                    return config ?? new SystemConfiguration();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading system config: {ex.Message}");
            }

            return new SystemConfiguration();
        }

        /// <summary>
        /// Load camera layout configuration
        /// </summary>
        public LayoutConfiguration LoadLayoutConfiguration()
        {
            try
            {
                if (File.Exists(_layoutConfigPath))
                {
                    var json = File.ReadAllText(_layoutConfigPath);
                    var config = JsonSerializer.Deserialize<LayoutConfiguration>(json, JsonOptions);
                    return config ?? new LayoutConfiguration();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading layout config: {ex.Message}");
            }

            return new LayoutConfiguration();
        }

        /// <summary>
        /// Save system configuration
        /// </summary>
        public bool SaveSystemConfiguration(SystemConfiguration config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_systemConfigPath, json);
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "SaveSystemConfiguration", AuditOutcome.Success,
                    source: nameof(ConfigurationService),
                    details: $"PlcVendor={config.PlcVendor}, IoBoards={config.IoBoards?.Count ?? 0}, Cameras={config.Cameras?.Count ?? 0}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving system config: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "SaveSystemConfiguration", AuditOutcome.Failure,
                    source: nameof(ConfigurationService),
                    details: $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Save camera layout configuration
        /// </summary>
        public bool SaveLayoutConfiguration(LayoutConfiguration config)
        {
            try
            {
                config.SavedAt = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_layoutConfigPath, json);
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "SaveLayoutConfiguration", AuditOutcome.Success,
                    source: nameof(ConfigurationService));
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving layout config: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "SaveLayoutConfiguration", AuditOutcome.Failure,
                    source: nameof(ConfigurationService),
                    details: $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load PLC signal configuration for AutoProcess
        /// </summary>
        public PlcSignalConfiguration LoadPlcSignalConfiguration()
        {
            try
            {
                if (File.Exists(_plcSignalConfigPath))
                {
                    var json = File.ReadAllText(_plcSignalConfigPath);
                    var config = JsonSerializer.Deserialize<PlcSignalConfiguration>(json, JsonOptions);
                    return config ?? new PlcSignalConfiguration();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading PLC signal config: {ex.Message}");
            }

            return new PlcSignalConfiguration();
        }

        /// <summary>
        /// Save PLC signal configuration for AutoProcess
        /// </summary>
        public bool SavePlcSignalConfiguration(PlcSignalConfiguration config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_plcSignalConfigPath, json);
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "SavePlcSignalConfiguration", AuditOutcome.Success,
                    source: nameof(ConfigurationService));
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving PLC signal config: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "SavePlcSignalConfiguration", AuditOutcome.Failure,
                    source: nameof(ConfigurationService),
                    details: $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }
    }
}
