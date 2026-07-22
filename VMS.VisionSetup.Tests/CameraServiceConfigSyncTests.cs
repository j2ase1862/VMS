using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using VMS.Camera.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// AppSetup(system_config.json SystemConfiguration 형식) ↔ VisionSetup CameraService
    /// 간 카메라 설정 연동 회귀 테스트 (현장 검증 2026-07-22).
    /// ① AppSetup 이 모든 카메라에 직렬화하는 boardType 기본값("SOLIOS")이 GigE 카메라의
    ///    ipAddress 를 덮어써 CameraInfo Manager 의 Connection String 이 비연동되던 결함.
    /// ② VisionSetup 저장 시 파일 전체를 CameraRegistry 형식으로 교체해 AppSetup 의
    ///    PLC/로봇/보안모드 설정이 소실되던 결함.
    /// </summary>
    public class CameraServiceConfigSyncTests
    {
        private const string SystemConfigJson = """
        {
          "applicationName": "BODA Vision System",
          "plcVendor": "Mitsubishi",
          "securityMode": "Production",
          "isRobotEnabled": true,
          "robotIpAddress": "192.168.0.200",
          "cameras": [
            {
              "id": "cam-1",
              "name": "Top Camera",
              "ipAddress": "192.168.1.60",
              "manufacturer": "HIK",
              "cameraType": "AreaScan2D",
              "isEnabled": true,
              "exposure": 5000,
              "gain": 1.5,
              "boardType": "SOLIOS",
              "boardNumber": 0,
              "digitizerNumber": 0,
              "dcfFilePath": ""
            },
            {
              "id": "cam-2",
              "name": "Grabber Camera",
              "ipAddress": "",
              "manufacturer": "Matrox",
              "cameraType": "LineScan2D",
              "isEnabled": true,
              "boardType": "RAPIXO",
              "boardNumber": 1,
              "digitizerNumber": 2,
              "dcfFilePath": "C:/dcf/line.dcf"
            }
          ]
        }
        """;

        private static List<CameraInfo> Load(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return CameraService.LoadFromSystemConfiguration(doc.RootElement);
        }

        [Fact]
        public void Load_GigECamera_KeepsIpAddress_DespiteDefaultBoardType()
        {
            var cameras = Load(SystemConfigJson);

            var gige = cameras.Single(c => c.Id == "cam-1");
            // boardType 기본값("SOLIOS")이 ipAddress 를 덮어쓰면 안 된다
            Assert.Equal("192.168.1.60", gige.ConnectionString);
            Assert.Equal("HIK", gige.Manufacturer);
        }

        [Fact]
        public void Load_FrameGrabberCamera_UsesBoardTypeAsConnectionString()
        {
            var cameras = Load(SystemConfigJson);

            var grabber = cameras.Single(c => c.Id == "cam-2");
            Assert.Equal("RAPIXO", grabber.ConnectionString);
            Assert.Equal(1, grabber.BoardNumber);
            Assert.Equal(2, grabber.DigitizerNumber);
            Assert.Equal("C:/dcf/line.dcf", grabber.DcfFilePath);
        }

        [Fact]
        public void Merge_UpdatesIpAddress_AndPreservesNonCameraSettings()
        {
            var root = (JsonObject)JsonNode.Parse(SystemConfigJson)!;
            var cameras = Load(SystemConfigJson);
            cameras.Single(c => c.Id == "cam-1").ConnectionString = "192.168.1.99";

            CameraService.MergeCamerasIntoSystemConfiguration(root, cameras);

            // AppSetup 전역 설정 보존
            Assert.Equal("BODA Vision System", (string?)root["applicationName"]);
            Assert.Equal("Mitsubishi", (string?)root["plcVendor"]);
            Assert.Equal("Production", (string?)root["securityMode"]);
            Assert.True((bool?)root["isRobotEnabled"]);

            // 갱신된 IP 반영
            var cam1 = ((JsonArray)root["cameras"]!).OfType<JsonObject>()
                .Single(o => (string?)o["id"] == "cam-1");
            Assert.Equal("192.168.1.99", (string?)cam1["ipAddress"]);
        }

        [Fact]
        public void Merge_PreservesAppSetupOnlyCameraFields()
        {
            var root = (JsonObject)JsonNode.Parse(SystemConfigJson)!;
            var cameras = Load(SystemConfigJson);

            CameraService.MergeCamerasIntoSystemConfiguration(root, cameras);

            // VisionSetup CameraInfo 에 없는 exposure/gain 은 기존 값 유지
            var cam1 = ((JsonArray)root["cameras"]!).OfType<JsonObject>()
                .Single(o => (string?)o["id"] == "cam-1");
            Assert.Equal(5000, (int?)cam1["exposure"]);
            Assert.Equal(1.5, (double?)cam1["gain"]);
        }

        [Fact]
        public void Merge_FrameGrabber_WritesBoardType_NotIpAddress()
        {
            var root = (JsonObject)JsonNode.Parse(SystemConfigJson)!;
            var cameras = Load(SystemConfigJson);
            cameras.Single(c => c.Id == "cam-2").ConnectionString = "SOLIOS";

            CameraService.MergeCamerasIntoSystemConfiguration(root, cameras);

            var cam2 = ((JsonArray)root["cameras"]!).OfType<JsonObject>()
                .Single(o => (string?)o["id"] == "cam-2");
            Assert.Equal("SOLIOS", (string?)cam2["boardType"]);
            // 프레임 그래버는 ipAddress 를 건드리지 않는다 (기존 값 유지)
            Assert.Equal("", (string?)cam2["ipAddress"]);
        }

        [Fact]
        public void Merge_AddsNewCamera_AndRemovesDeletedCamera()
        {
            var root = (JsonObject)JsonNode.Parse(SystemConfigJson)!;
            var cameras = Load(SystemConfigJson);
            cameras.RemoveAll(c => c.Id == "cam-2");
            cameras.Add(new CameraInfo
            {
                Id = "cam-3",
                Name = "Side Camera",
                Manufacturer = "Basler",
                ConnectionString = "192.168.1.70",
                IsEnabled = true
            });

            CameraService.MergeCamerasIntoSystemConfiguration(root, cameras);

            var ids = ((JsonArray)root["cameras"]!).OfType<JsonObject>()
                .Select(o => (string?)o["id"]).ToList();
            Assert.Equal(new[] { "cam-1", "cam-3" }, ids);

            var cam3 = ((JsonArray)root["cameras"]!).OfType<JsonObject>()
                .Single(o => (string?)o["id"] == "cam-3");
            Assert.Equal("192.168.1.70", (string?)cam3["ipAddress"]);
            Assert.Equal("Basler", (string?)cam3["manufacturer"]);
        }

        [Fact]
        public void Merge_UnknownManufacturer_KeepsExistingEnum_OrFallsBackToOther()
        {
            var root = (JsonObject)JsonNode.Parse(SystemConfigJson)!;
            var cameras = Load(SystemConfigJson);
            // 기존 카메라에 파싱 불가한 제조사 입력 → AppSetup enum 파싱이 깨지지 않게 기존 값 유지
            cameras.Single(c => c.Id == "cam-1").Manufacturer = "SomeVendorX";
            // 신규 카메라에 파싱 불가한 제조사 → Other 로 기록
            cameras.Add(new CameraInfo { Id = "cam-9", Name = "New", Manufacturer = "SomeVendorX" });

            CameraService.MergeCamerasIntoSystemConfiguration(root, cameras);

            var arr = ((JsonArray)root["cameras"]!).OfType<JsonObject>().ToList();
            Assert.Equal("HIK", (string?)arr.Single(o => (string?)o["id"] == "cam-1")["manufacturer"]);
            Assert.Equal("Other", (string?)arr.Single(o => (string?)o["id"] == "cam-9")["manufacturer"]);
        }

        [Fact]
        public void Load_RoundTripFields_ReadBackWhenPresent()
        {
            var root = (JsonObject)JsonNode.Parse(SystemConfigJson)!;
            var cameras = Load(SystemConfigJson);
            var cam1 = cameras.Single(c => c.Id == "cam-1");
            cam1.Model = "acA1920-40gm";
            cam1.SerialNumber = "SN-001";
            cam1.Width = 2448;
            cam1.Height = 2048;

            CameraService.MergeCamerasIntoSystemConfiguration(root, cameras);
            var reloaded = Load(root.ToJsonString());

            var back = reloaded.Single(c => c.Id == "cam-1");
            Assert.Equal("acA1920-40gm", back.Model);
            Assert.Equal("SN-001", back.SerialNumber);
            Assert.Equal(2448, back.Width);
            Assert.Equal(2048, back.Height);
        }
    }
}
