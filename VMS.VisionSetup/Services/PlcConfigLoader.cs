using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.PLC.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 공유 AppData의 system_config.json에서 PLC 연결 설정만 추출.
    /// VMS 프로젝트 참조 없이 독립 동작.
    /// </summary>
    public static class PlcConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// system_config.json에서 PLC 연결 설정을 로드.
        /// PlcVendor가 None이면 null 반환.
        /// </summary>
        public static PlcConnectionConfig? LoadFromAppData()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var configPath = Path.Combine(appData, "BODA VISION AI", "system_config.json");

                if (!File.Exists(configPath))
                    return null;

                var json = File.ReadAllText(configPath);
                var dto = JsonSerializer.Deserialize<PlcConfigDto>(json, JsonOptions);
                if (dto == null || dto.PlcVendor == PlcVendor.None)
                    return null;

                return new PlcConnectionConfig
                {
                    Vendor = dto.PlcVendor,
                    CommunicationType = dto.CommunicationType,
                    IpAddress = dto.PlcIpAddress,
                    Port = dto.PlcPort > 0 ? dto.PlcPort : PLC.Services.PlcConnectionFactory.GetDefaultPort(dto.PlcVendor),

                    // Modbus
                    UnitId = dto.ModbusUnitId,

                    // Serial
                    SerialPortName = dto.SerialPortName,
                    BaudRate = dto.BaudRate,
                    DataBits = dto.DataBits,
                    Parity = dto.Parity,
                    StopBits = dto.StopBits,

                    // Performance
                    PollingIntervalMs = dto.PollingIntervalMs,
                    UseHeartbeat = dto.UseHeartbeat,
                    HeartbeatAddress = dto.HeartbeatAddress,
                    AutoReconnect = dto.AutoReconnect,
                    WriteMode = dto.WriteMode,
                    EndianMode = dto.EndianMode
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlcConfigLoader] Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// system_config.json의 PLC 관련 필드만 매핑하는 내부 DTO
        /// </summary>
        private class PlcConfigDto
        {
            public PlcVendor PlcVendor { get; set; } = PlcVendor.None;
            public PlcCommunicationType CommunicationType { get; set; } = PlcCommunicationType.Ethernet;
            public string PlcIpAddress { get; set; } = "192.168.0.100";
            public int PlcPort { get; set; }

            public byte ModbusUnitId { get; set; } = 255;

            public string SerialPortName { get; set; } = "COM1";
            public int BaudRate { get; set; } = 115200;
            public int DataBits { get; set; } = 8;
            public PlcSerialParity Parity { get; set; } = PlcSerialParity.None;
            public PlcSerialStopBits StopBits { get; set; } = PlcSerialStopBits.One;

            public int PollingIntervalMs { get; set; } = 20;
            public bool UseHeartbeat { get; set; }
            public string HeartbeatAddress { get; set; } = string.Empty;
            public bool AutoReconnect { get; set; } = true;
            public PlcWriteMode WriteMode { get; set; } = PlcWriteMode.Handshake;
            public PlcEndianMode EndianMode { get; set; } = PlcEndianMode.LittleEndian;
        }
    }
}
