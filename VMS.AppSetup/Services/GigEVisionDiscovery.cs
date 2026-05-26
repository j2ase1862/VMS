using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VMS.AppSetup.Services
{
    /// <summary>한 GigE Vision 카메라의 디스커버리 응답 메타데이터.</summary>
    public class GigEVisionDevice
    {
        public string IpAddress { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string UserDefinedName { get; set; } = string.Empty;
        public string DeviceVersion { get; set; } = string.Empty;

        public override string ToString() =>
            string.IsNullOrEmpty(UserDefinedName)
                ? $"{Manufacturer} {Model} ({IpAddress})"
                : $"{UserDefinedName} · {Manufacturer} {Model} ({IpAddress})";
    }

    /// <summary>
    /// GigE Vision 표준 Discovery (GVCP) 구현 — SDK 없이도 GigE 카메라 자동 탐색.
    /// UDP 3956 포트로 DISCOVERY_CMD 브로드캐스트 → 카메라가 DISCOVERY_ACK 응답.
    /// Hikrobot/Basler/IDS/Allied Vision 등 표준 준수 카메라 모두 응답.
    /// </summary>
    public static class GigEVisionDiscovery
    {
        private const int GvcpPort = 3956;

        // GVCP DISCOVERY_CMD: header(8) = flag(0x42) + ack-broadcast(0x11) + cmd(0x0002) + len(0x0000) + reqId(2)
        private static readonly byte[] DiscoveryRequest =
        {
            0x42, 0x11, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01
        };

        /// <summary>
        /// 모든 활성 IPv4 네트워크 인터페이스로 broadcast → 응답 카메라 리스트 반환.
        /// </summary>
        public static async Task<List<GigEVisionDevice>> DiscoverAsync(int timeoutMs = 2000)
        {
            var devices = new List<GigEVisionDevice>();
            var seen = new HashSet<string>();  // dedupe by IP
            var lockObj = new object();

            // 각 NIC별로 broadcast 시도 — multi-NIC 환경 (산업용)
            var tasks = new List<Task>();
            foreach (var localAddr in GetLocalUnicastAddresses())
            {
                tasks.Add(BroadcastAndCollectAsync(localAddr, timeoutMs, dev =>
                {
                    lock (lockObj)
                    {
                        if (seen.Add(dev.IpAddress))
                            devices.Add(dev);
                    }
                }));
            }

            await Task.WhenAll(tasks);
            return devices;
        }

        private static IEnumerable<IPAddress> GetLocalUnicastAddresses()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ua.Address)) continue;
                    yield return ua.Address;
                }
            }
        }

        private static async Task BroadcastAndCollectAsync(
            IPAddress localAddr, int timeoutMs, Action<GigEVisionDevice> onFound)
        {
            UdpClient? udp = null;
            try
            {
                udp = new UdpClient(new IPEndPoint(localAddr, 0))
                {
                    EnableBroadcast = true
                };

                // 브로드캐스트 전송
                var endpoint = new IPEndPoint(IPAddress.Broadcast, GvcpPort);
                await udp.SendAsync(DiscoveryRequest, DiscoveryRequest.Length, endpoint);

                // 응답 수신 — timeout 까지 반복
                using var cts = new CancellationTokenSource(timeoutMs);
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        var result = await udp.ReceiveAsync(cts.Token);
                        var device = ParseDiscoveryAck(result.Buffer);
                        if (device != null) onFound(device);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (SocketException) { break; }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GigE Discovery on {localAddr} 실패: {ex.Message}");
            }
            finally
            {
                udp?.Dispose();
            }
        }

        /// <summary>
        /// GVCP DISCOVERY_ACK 페이로드 파싱.
        /// 총 248 bytes = 8 header + 240 payload. ack 명령코드는 0x0003.
        /// internal — 단위 테스트가 직접 호출.
        /// </summary>
        internal static GigEVisionDevice? ParseDiscoveryAck(byte[] data)
        {
            // 최소 길이 검증
            if (data.Length < 248) return null;

            // command code (offset 2-3, BE) == 0x0003 (DISCOVERY_ACK)
            if (data[2] != 0x00 || data[3] != 0x03) return null;

            // payload는 헤더(8) 다음부터. 아래 offset은 packet 시작 기준.
            // GVCP DISCOVERY_ACK 페이로드 layout (RFC GigE Vision):
            //   offset 16-21: MAC address (6 bytes)
            //   offset 44-47: current IP address (4 bytes, BE)
            //   offset 72-103: Manufacturer name (32 bytes ASCII)
            //   offset 104-135: Model name (32 bytes)
            //   offset 136-167: Device version (32 bytes)
            //   offset 168-215: Manufacturer info (48 bytes)
            //   offset 216-231: Serial number (16 bytes)
            //   offset 232-247: User-defined name (16 bytes)

            try
            {
                var mac = string.Join(":", Enumerable.Range(0, 6)
                    .Select(i => data[16 + i].ToString("X2")));
                var ip = $"{data[44]}.{data[45]}.{data[46]}.{data[47]}";

                return new GigEVisionDevice
                {
                    MacAddress = mac,
                    IpAddress = ip,
                    Manufacturer = ReadAsciiZ(data, 72, 32),
                    Model = ReadAsciiZ(data, 104, 32),
                    DeviceVersion = ReadAsciiZ(data, 136, 32),
                    SerialNumber = ReadAsciiZ(data, 216, 16),
                    UserDefinedName = ReadAsciiZ(data, 232, 16)
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>NULL-terminated 또는 fixed-length ASCII 문자열 읽기.</summary>
        private static string ReadAsciiZ(byte[] data, int offset, int maxLen)
        {
            if (offset + maxLen > data.Length) maxLen = data.Length - offset;
            int end = offset;
            while (end < offset + maxLen && data[end] != 0) end++;
            return Encoding.ASCII.GetString(data, offset, end - offset).Trim();
        }
    }
}
