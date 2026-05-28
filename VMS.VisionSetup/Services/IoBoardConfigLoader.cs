using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.PLC.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 공유 AppData 의 system_config.json 에서 IoBoards 섹션만 추출.
    /// VMS 본 앱(host) 미경유로 VMS.VisionSetup 이 standalone 또는 별도 프로세스로 떠도
    /// SequenceEditor 가 PLC + IO 보드 디바이스 목록을 모두 확보하도록.
    /// VMS 프로젝트 참조 없이 독립 동작 (PlcConfigLoader 와 동일 패턴).
    /// </summary>
    public static class IoBoardConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// system_config.json 에서 IoBoards 리스트를 로드. 파일 없거나 키 없으면 빈 리스트.
        /// IsEnabled=false 보드는 제외, DeviceId 가 비어있는 항목도 제외.
        /// </summary>
        public static List<IoDeviceConfig> LoadFromAppData()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var configPath = Path.Combine(appData, "BODA VISION AI", "system_config.json");

                if (!File.Exists(configPath))
                    return new List<IoDeviceConfig>();

                var json = File.ReadAllText(configPath);
                var dto = JsonSerializer.Deserialize<IoBoardsDto>(json, JsonOptions);
                if (dto?.IoBoards == null || dto.IoBoards.Count == 0)
                    return new List<IoDeviceConfig>();

                var result = new List<IoDeviceConfig>(dto.IoBoards.Count);
                foreach (var b in dto.IoBoards)
                {
                    if (!b.IsEnabled) continue;
                    if (string.IsNullOrWhiteSpace(b.DeviceId)) continue;
                    result.Add(b);
                }
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IoBoardConfigLoader] Error: {ex.Message}");
                return new List<IoDeviceConfig>();
            }
        }

        /// <summary>
        /// IoBoardVendor / Model 조합을 IoDeviceType 으로 매핑. Factory 와 동일 규약.
        /// </summary>
        public static IoDeviceType ResolveDeviceType(IoDeviceConfig config) => config.Vendor switch
        {
            IoBoardVendor.AdLink => IoDeviceType.AdLinkPci743x,
            IoBoardVendor.Advantech => IoDeviceType.AdvantechPci17xx,
            _ => IoDeviceType.Unknown
        };

        private class IoBoardsDto
        {
            public List<IoDeviceConfig> IoBoards { get; set; } = new();
        }
    }
}
