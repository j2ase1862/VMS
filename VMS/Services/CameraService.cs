using VMS.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.Services
{
    /// <summary>
    /// Service for managing global camera registry
    /// </summary>
    public class CameraService
    {
        private static CameraService? _instance;
        public static CameraService Instance => _instance ??= new CameraService();

        private readonly string _camerasFilePath;
        private List<CameraInfo> _cameras = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private CameraService()
        {
            var configDir = ConfigurationService.Instance.ConfigDirectory;
            _camerasFilePath = Path.Combine(configDir, "cameras.json");
        }

        /// <summary>
        /// Load camera registry from file
        /// </summary>
        public List<CameraInfo> LoadCameraRegistry()
        {
            try
            {
                if (File.Exists(_camerasFilePath))
                {
                    var json = File.ReadAllText(_camerasFilePath);
                    var registry = JsonSerializer.Deserialize<CameraRegistry>(json, JsonOptions);
                    _cameras = registry?.Cameras ?? new List<CameraInfo>();
                    return _cameras;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading camera registry: {ex.Message}");
            }

            _cameras = new List<CameraInfo>();
            return _cameras;
        }

        /// <summary>
        /// Save camera registry to file
        /// </summary>
        public bool SaveCameraRegistry(List<CameraInfo>? cameras = null)
        {
            try
            {
                if (cameras != null)
                    _cameras = cameras;

                var registry = new CameraRegistry { Cameras = _cameras };
                var json = JsonSerializer.Serialize(registry, JsonOptions);
                File.WriteAllText(_camerasFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving camera registry: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Add a new camera to the registry
        /// </summary>
        public void AddCamera(CameraInfo camera)
        {
            _cameras.Add(camera);
            SaveCameraRegistry();
        }

        /// <summary>
        /// Remove a camera from the registry
        /// </summary>
        public bool RemoveCamera(string id)
        {
            var camera = _cameras.Find(c => c.Id == id);
            if (camera != null)
            {
                _cameras.Remove(camera);
                SaveCameraRegistry();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get a camera by ID
        /// </summary>
        public CameraInfo? GetCamera(string id)
        {
            return _cameras.Find(c => c.Id == id);
        }

        /// <summary>
        /// Get all cameras
        /// </summary>
        public List<CameraInfo> GetAllCameras()
        {
            return _cameras;
        }

        /// <summary>
        /// Update an existing camera
        /// </summary>
        public bool UpdateCamera(CameraInfo camera)
        {
            var index = _cameras.FindIndex(c => c.Id == camera.Id);
            if (index >= 0)
            {
                _cameras[index] = camera;
                SaveCameraRegistry();
                return true;
            }
            return false;
        }
    }
}
