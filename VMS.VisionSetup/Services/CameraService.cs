using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Diagnostics;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 카메라 레지스트리 관리 서비스
    /// </summary>
    public class CameraService : ICameraService
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
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private CameraService()
        {
            _appDataPath = GetAppDataPath();
            _cameraRegistryPath = Path.Combine(_appDataPath, "system_config.json");
            EnsureDirectoryExists();
        }

        private string GetAppDataPath()
        {
            return VMS.Camera.Configuration.AppDataPaths.Root;
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
        /// AppSetup이 저장한 SystemConfiguration 형식과 CameraRegistry 형식 모두 지원
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
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // AppSetup이 저장한 SystemConfiguration 형식 감지 (applicationName 또는 plcVendor 필드 존재)
                if (root.TryGetProperty("applicationName", out _) ||
                    root.TryGetProperty("plcVendor", out _))
                {
                    _cameras = LoadFromSystemConfiguration(root);
                    Debug.WriteLine($"SystemConfiguration 형식에서 카메라 {_cameras.Count}대 로드 완료");
                }
                else
                {
                    // 기존 CameraRegistry 형식
                    var registry = JsonSerializer.Deserialize<CameraRegistry>(json, JsonOptions);
                    _cameras = registry?.Cameras ?? new List<CameraInfo>();
                    Debug.WriteLine($"CameraRegistry 형식에서 카메라 {_cameras.Count}대 로드 완료");
                }

                return _cameras;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"카메라 레지스트리 로드 실패: {ex.Message}");
                _cameras = new List<CameraInfo>();
                return _cameras;
            }
        }

        /// <summary>
        /// SystemConfiguration JSON에서 CameraConfiguration → CameraInfo 변환
        /// </summary>
        internal static List<CameraInfo> LoadFromSystemConfiguration(JsonElement root)
        {
            var cameras = new List<CameraInfo>();

            if (!root.TryGetProperty("cameras", out var camerasElement))
                return cameras;

            foreach (var cam in camerasElement.EnumerateArray())
            {
                var info = new CameraInfo
                {
                    Id = cam.TryGetProperty("id", out var id)
                        ? id.GetString() ?? Guid.NewGuid().ToString()
                        : Guid.NewGuid().ToString(),
                    Name = cam.TryGetProperty("name", out var name)
                        ? name.GetString() ?? string.Empty
                        : string.Empty,
                    ConnectionString = cam.TryGetProperty("ipAddress", out var ip)
                        ? ip.GetString() ?? string.Empty
                        : string.Empty,
                    IsEnabled = !cam.TryGetProperty("isEnabled", out var enabled) || enabled.GetBoolean(),
                };

                // Manufacturer: enum 문자열 → CameraInfo.Manufacturer (string)
                if (cam.TryGetProperty("manufacturer", out var mfr) &&
                    mfr.ValueKind == JsonValueKind.String)
                {
                    info.Manufacturer = mfr.GetString() ?? "Other";
                }

                // CameraType: enum 문자열 → CameraType enum
                if (cam.TryGetProperty("cameraType", out var ct) &&
                    ct.ValueKind == JsonValueKind.String)
                {
                    if (Enum.TryParse<CameraType>(ct.GetString(), out var cameraType))
                        info.CameraType = cameraType;
                }

                // Frame Grabber (Matrox/Dalsa) 설정 — boardType 은 프레임 그래버에서만
                // 연결 문자열이다. AppSetup 은 모든 카메라에 boardType 기본값("SOLIOS")을
                // 직렬화하므로, 제조사 구분 없이 덮어쓰면 GigE 카메라의 ipAddress 가
                // 사라진다 (현장 검증 2026-07-22: Connection String 미연동 원인).
                if (IsFrameGrabber(info.Manufacturer) &&
                    cam.TryGetProperty("boardType", out var bt) && bt.ValueKind == JsonValueKind.String)
                    info.ConnectionString = bt.GetString() ?? info.ConnectionString;
                if (cam.TryGetProperty("boardNumber", out var bn) && bn.ValueKind == JsonValueKind.Number)
                    info.BoardNumber = bn.GetInt32();
                if (cam.TryGetProperty("digitizerNumber", out var dn) && dn.ValueKind == JsonValueKind.Number)
                    info.DigitizerNumber = dn.GetInt32();
                if (cam.TryGetProperty("dcfFilePath", out var dcf) && dcf.ValueKind == JsonValueKind.String)
                    info.DcfFilePath = dcf.GetString() ?? string.Empty;

                // VisionSetup 이 병합 저장하는 부가 필드 — AppSetup 은 기록하지 않으므로 있을 때만
                if (cam.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
                    info.Model = model.GetString() ?? string.Empty;
                if (cam.TryGetProperty("serialNumber", out var sn) && sn.ValueKind == JsonValueKind.String)
                    info.SerialNumber = sn.GetString() ?? string.Empty;
                if (cam.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number)
                    info.Width = w.GetInt32();
                if (cam.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number)
                    info.Height = h.GetInt32();

                cameras.Add(info);
            }

            return cameras;
        }

        /// <summary>
        /// 카메라 레지스트리를 파일에 저장.
        /// 파일이 AppSetup 의 SystemConfiguration 형식이면 cameras 배열만 갱신하고
        /// 나머지 설정(PLC/로봇/보안모드/Web 등)은 그대로 보존한다 — 파일 전체를
        /// CameraRegistry 형식으로 교체하면 AppSetup 설정이 통째로 소실된다
        /// (현장 검증 2026-07-22).
        /// </summary>
        public bool SaveCameraRegistry(List<CameraInfo>? cameras = null)
        {
            try
            {
                if (cameras != null)
                    _cameras = cameras;

                EnsureDirectoryExists();

                if (File.Exists(_cameraRegistryPath) &&
                    JsonNode.Parse(File.ReadAllText(_cameraRegistryPath)) is JsonObject rootNode &&
                    (rootNode.ContainsKey("applicationName") || rootNode.ContainsKey("plcVendor")))
                {
                    MergeCamerasIntoSystemConfiguration(rootNode, _cameras);
                    File.WriteAllText(_cameraRegistryPath,
                        rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    return true;
                }

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
        /// SystemConfiguration JSON 루트의 "cameras" 배열을 CameraInfo 목록으로 갱신.
        /// 같은 id 의 기존 entry 는 노출/게인 등 AppSetup 전용 필드를 보존한 채 갱신하고,
        /// 목록에 없는 entry 는 제거, 새 카메라는 추가한다.
        /// </summary>
        internal static void MergeCamerasIntoSystemConfiguration(JsonObject root, List<CameraInfo> cameras)
        {
            var existing = root["cameras"] as JsonArray;
            var merged = new JsonArray();

            foreach (var cam in cameras)
            {
                var entry = existing?.OfType<JsonObject>()
                    .FirstOrDefault(o => (string?)o["id"] == cam.Id)
                    ?.DeepClone() as JsonObject ?? new JsonObject();

                entry["id"] = cam.Id;
                entry["name"] = cam.Name;
                entry["isEnabled"] = cam.IsEnabled;
                entry["cameraType"] = cam.CameraType.ToString();

                // ConnectionString 의 의미가 제조사에 따라 다르다 — 프레임 그래버는
                // boardType(SOLIOS 등), 그 외는 ipAddress. LoadFromSystemConfiguration 과 대칭.
                if (IsFrameGrabber(cam.Manufacturer))
                    entry["boardType"] = cam.ConnectionString;
                else
                    entry["ipAddress"] = cam.ConnectionString;

                // AppSetup/VMS 는 manufacturer 를 enum 문자열로 파싱하므로 알 수 없는
                // 값을 쓰면 설정 로드가 깨진다 — 파싱 가능한 값만 기록.
                if (Enum.TryParse<CameraManufacturer>(cam.Manufacturer, ignoreCase: true, out var mfr))
                    entry["manufacturer"] = mfr.ToString();
                else if (entry["manufacturer"] is null)
                    entry["manufacturer"] = nameof(CameraManufacturer.Other);

                entry["boardNumber"] = cam.BoardNumber;
                entry["digitizerNumber"] = cam.DigitizerNumber;
                entry["dcfFilePath"] = cam.DcfFilePath;

                // VisionSetup 전용 부가 필드 — AppSetup/VMS 파서는 무시(unknown property)
                entry["model"] = cam.Model;
                entry["serialNumber"] = cam.SerialNumber;
                entry["width"] = cam.Width;
                entry["height"] = cam.Height;

                merged.Add(entry);
            }

            root["cameras"] = merged;
        }

        /// <summary>연결 문자열이 IP 가 아닌 보드 타입인 프레임 그래버 제조사 여부</summary>
        private static bool IsFrameGrabber(string manufacturer) =>
            manufacturer.Equals(nameof(CameraManufacturer.Matrox), StringComparison.OrdinalIgnoreCase) ||
            manufacturer.Equals(nameof(CameraManufacturer.Dalsa), StringComparison.OrdinalIgnoreCase);

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
