using VMS.VisionSetup.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 카메라 레지스트리 관리 서비스
    /// </summary>
    public class CameraService
    {
        private static readonly Lazy<CameraService> _instance = new(() => new CameraService());
        public static CameraService Instance => _instance.Value;

        private readonly string _appDataPath;
        private readonly string _cameraRegistryPath;
        private List<CameraInfo> _cameras = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private CameraService()
        {
            _appDataPath = GetAppDataPath();
            _cameraRegistryPath = Path.Combine(_appDataPath, "system_config.json");
            EnsureDirectoryExists();
        }

        private string GetAppDataPath()
        {
            // Use local AppData for BODA VISION AI
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "BODA VISION AI");
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_appDataPath))
            {
                Directory.CreateDirectory(_appDataPath);
            }
        }

        /// <summary>
        /// 카메라 레지스트리를 파일에서 로드
        /// </summary>
        public List<CameraInfo> LoadCameraRegistry()
        {
            try
            {
                if (!File.Exists(_cameraRegistryPath))
                {
                    _cameras = new List<CameraInfo>();
                    return _cameras;
                }

                var json = File.ReadAllText(_cameraRegistryPath);
                var registry = JsonSerializer.Deserialize<CameraRegistry>(json, JsonOptions);
                _cameras = registry?.Cameras ?? new List<CameraInfo>();
                return _cameras;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"카메라 레지스트리 로드 실패: {ex.Message}");
                _cameras = new List<CameraInfo>();
                return _cameras;
            }
        }

        /// <summary>
        /// 카메라 레지스트리를 파일에 저장
        /// </summary>
        public bool SaveCameraRegistry(List<CameraInfo>? cameras = null)
        {
            try
            {
                if (cameras != null)
                    _cameras = cameras;

                EnsureDirectoryExists();

                var registry = new CameraRegistry { Cameras = _cameras };
                var json = JsonSerializer.Serialize(registry, JsonOptions);
                File.WriteAllText(_cameraRegistryPath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"카메라 레지스트리 저장 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 새 카메라 추가
        /// </summary>
        public bool AddCamera(CameraInfo camera)
        {
            if (camera == null) return false;

            // 동일 ID가 이미 존재하면 교체
            var existing = _cameras.FirstOrDefault(c => c.Id == camera.Id);
            if (existing != null)
            {
                _cameras.Remove(existing);
            }

            _cameras.Add(camera);
            return SaveCameraRegistry();
        }

        /// <summary>
        /// 카메라 업데이트
        /// </summary>
        public bool UpdateCamera(CameraInfo camera)
        {
            if (camera == null) return false;

            var existing = _cameras.FirstOrDefault(c => c.Id == camera.Id);
            if (existing == null) return false;

            var index = _cameras.IndexOf(existing);
            _cameras[index] = camera;
            return SaveCameraRegistry();
        }

        /// <summary>
        /// 카메라 제거
        /// </summary>
        public bool RemoveCamera(string id)
        {
            var camera = _cameras.FirstOrDefault(c => c.Id == id);
            if (camera == null) return false;

            _cameras.Remove(camera);
            return SaveCameraRegistry();
        }

        /// <summary>
        /// ID로 카메라 조회
        /// </summary>
        public CameraInfo? GetCamera(string id)
        {
            return _cameras.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// 모든 카메라 목록 반환
        /// </summary>
        public List<CameraInfo> GetAllCameras()
        {
            return _cameras.ToList();
        }

        /// <summary>
        /// 활성화된 카메라만 반환
        /// </summary>
        public List<CameraInfo> GetEnabledCameras()
        {
            return _cameras.Where(c => c.IsEnabled).ToList();
        }

        /// <summary>
        /// 제조사별 카메라 필터링
        /// </summary>
        public List<CameraInfo> GetCamerasByManufacturer(string manufacturer)
        {
            return _cameras.Where(c => c.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// 새 카메라 인스턴스 생성 (기본값으로)
        /// </summary>
        public CameraInfo CreateNewCamera()
        {
            int count = _cameras.Count + 1;
            return new CameraInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"Camera {count}",
                Manufacturer = "Unknown",
                Model = "Unknown",
                SerialNumber = string.Empty,
                Width = 1920,
                Height = 1080,
                ConnectionString = "192.168.1.100",
                IsEnabled = true
            };
        }

        /// <summary>
        /// 시리얼 번호로 카메라 검색
        /// </summary>
        public CameraInfo? FindCameraBySerialNumber(string serialNumber)
        {
            return _cameras.FirstOrDefault(c =>
                c.SerialNumber.Equals(serialNumber, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 연결 문자열로 카메라 검색
        /// </summary>
        public CameraInfo? FindCameraByConnectionString(string connectionString)
        {
            return _cameras.FirstOrDefault(c =>
                c.ConnectionString.Equals(connectionString, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 카메라 레지스트리 파일 경로 반환
        /// </summary>
        public string GetRegistryFilePath() => _cameraRegistryPath;

        /// <summary>
        /// AppData 폴더 경로 반환
        /// </summary>
        public string GetAppDataFolderPath() => _appDataPath;
    }
}
