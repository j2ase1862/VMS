using System.Threading.Tasks;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.PLC.Services;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// AUTO RUN 시작 때 PLC 에 접속하면서 제조사만 넘겨 IP · 포트가 기본값(192.168.0.100 · 0)으로 나가던 문제 —
    /// 설정 마법사에서 정한 접속 설정이 그대로 쓰여야 한다.
    /// </summary>
    public class AutoProcessPlcConnectConfigTests
    {
        private static AutoProcessService Create(SimulatedPlcConnection plc, PlcConnectionConfig? connectionConfig) =>
            new(
                plc,
                new PlcSignalConfiguration(),
                PlcVendor.Mitsubishi,
                grabFunc: _ => Task.FromResult(true),
                inspectFunc: _ => Task.FromResult(true),
                setResultFunc: (_, _) => { },
                resetFunc: _ => { },
                processSequence: new SequenceConfig(),   // Start 노드 없음 — 운전 루프는 곧바로 끝난다
                activitySignalFactory: () => null,
                connectionConfig: connectionConfig);

        [Fact]
        public async Task Start_connects_with_configured_address_and_port()
        {
            var plc = new SimulatedPlcConnection();
            var configured = new PlcConnectionConfig
            {
                Vendor = PlcVendor.Mitsubishi,
                IpAddress = "10.20.30.40",
                Port = 5007,
                ConnectTimeoutMs = 1234,
            };
            var svc = Create(plc, configured);

            await svc.StartAsync();
            await svc.StopAsync();

            Assert.NotNull(plc.LastConnectConfig);
            Assert.Equal("10.20.30.40", plc.LastConnectConfig!.IpAddress);
            Assert.Equal(5007, plc.LastConnectConfig.Port);
            Assert.Equal(1234, plc.LastConnectConfig.ConnectTimeoutMs);
            Assert.Equal(PlcVendor.Mitsubishi, plc.LastConnectConfig.Vendor);
        }

        [Fact]
        public async Task Start_without_config_falls_back_to_vendor_only()
        {
            var plc = new SimulatedPlcConnection();
            var svc = Create(plc, connectionConfig: null);

            await svc.StartAsync();
            await svc.StopAsync();

            Assert.NotNull(plc.LastConnectConfig);
            Assert.Equal(PlcVendor.Mitsubishi, plc.LastConnectConfig!.Vendor);
        }

        [Fact]
        public async Task Already_connected_plc_is_not_reconnected()
        {
            var plc = new SimulatedPlcConnection();
            await plc.ConnectAsync(new PlcConnectionConfig { Vendor = PlcVendor.Mitsubishi, IpAddress = "1.2.3.4", Port = 1 });
            var svc = Create(plc, new PlcConnectionConfig { Vendor = PlcVendor.Mitsubishi, IpAddress = "10.20.30.40", Port = 5007 });

            await svc.StartAsync();
            await svc.StopAsync();

            Assert.Equal("1.2.3.4", plc.LastConnectConfig!.IpAddress);
        }
    }
}
