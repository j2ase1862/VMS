using VMS.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.Services
{
    /// <summary>
    /// Service for loading system configuration from BODA.Setup
    /// </summary>
    public class ConfigurationService
    {
        private static ConfigurationService? _instance;
        public static ConfigurationService Instance => _instance ??= new ConfigurationService();

        private readonly string _configDirectory;
        private readonly string _systemConfigPath;
        private readonly string _layoutConfigPath;

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
        {
            _configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            _systemConfigPath = Path.Combine(_configDirectory, "system_config.json");
            _layoutConfigPath = Path.Combine(_configDirectory, "layout_config.json");

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
        /// Save camera layout configuration
        /// </summary>
        public bool SaveLayoutConfiguration(LayoutConfiguration config)
        {
            try
            {
                config.SavedAt = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_layoutConfigPath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving layout config: {ex.Message}");
                return false;
            }
        }
    }
}
