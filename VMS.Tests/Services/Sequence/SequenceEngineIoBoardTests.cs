using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.PLC.Services;
using VMS.Services.Sequence;
using Xunit;

namespace VMS.Tests.Services.Sequence
{
    /// <summary>
    /// IO 보드(DeviceId) 경로 검증 — PLC 없이 보드만으로 운전하는 현장 구성.
    ///
    /// 현장 사고(2026-08-14): PLC 벤더 None + ADLink 보드 구성에서 AUTO RUN 이 아무 반응 없이
    /// 멈춰 있었다. 원인은 보드 연결 실패가 은폐된 채 등록되어, InputCheck 가 읽는 순간 예외가
    /// 나고 그 예외가 조용히 삼켜진 것. 아래 테스트는 (1) 보드 라우팅이 실제로 동작하는지,
    /// (2) 미연결 보드의 예외가 SequenceError 로 표면화되는지를 고정한다 —
    /// (2) 는 AutoProcessService 의 오류 로깅·재시도 지연이 의존하는 계약이다.
    /// </summary>
    public class SequenceEngineIoBoardTests : IDisposable
    {
        private const string BoardId = "ADLink_1";

        private readonly SimulatedPlcConnection _plc = new();

        public void Dispose()
        {
            try { _plc.Dispose(); } catch { /* 무시 */ }
        }

        private SequenceEngine MakeEngine(IIoDeviceRegistry? registry,
            Func<string, IReadOnlyList<VMS.Interfaces.ToolInspectionResult>?>? toolResults = null)
        {
            return new SequenceEngine(
                _plc,
                PlcVendor.Mitsubishi,
                _ => Task.FromResult(true),
                _ => Task.FromResult(true),
                (_, _) => { },
                _ => { },
                getToolResultsFunc: toolResults,
                recipeChangeByIndexFunc: null,
                stepChangeFunc: null,
                ioRegistry: registry);
        }

        private static IIoDeviceRegistry MakeRegistry(IIoDevice device)
        {
            var registry = new IoDeviceRegistry();
            registry.Register(device);
            return registry;
        }

        private static SequenceNodeConfig Start(string id, string? next) => new()
        {
            Id = id, NodeType = SequenceNodeType.Start, Name = "Start", NextNodeId = next
        };

        private static SequenceNodeConfig End(string id) => new()
        {
            Id = id, NodeType = SequenceNodeType.End, Name = "End"
        };

        /// <summary>보드 채널을 기다리는 InputCheck — PlcAddress 는 채널 번호 문자열.</summary>
        private static SequenceNodeConfig BoardInputCheck(string id, int channel, string? next) => new()
        {
            Id = id,
            NodeType = SequenceNodeType.InputCheck,
            Name = "Wait Trigger",
            DeviceId = BoardId,
            PlcAddress = channel.ToString(),
            CheckMode = InputCheckMode.BitOn,
            TimeoutMs = 2000,
            NextNodeId = next
        };

        [Fact]
        public async Task InputCheck_WithBoardDeviceId_ReadsBoardChannel_NotPlc()
        {
            var board = new StubBoard();
            board.SetBit(4, true);
            var engine = MakeEngine(MakeRegistry(board));

            var config = new SequenceConfig
            {
                Name = "BoardTrigger",
                Nodes = new List<SequenceNodeConfig> { Start("s", "wait"), BoardInputCheck("wait", 4, "e"), End("e") }
            };

            SequenceErrorEventArgs? errorNode = null;
            engine.SequenceError += (_, e) => errorNode = e;

            await engine.RunAsync(config, CancellationToken.None);

            Assert.Null(errorNode);                     // 오류 없이 완주
            Assert.Contains(4, board.ReadChannels);     // 보드 채널 4 를 실제로 읽었다
        }

        [Fact]
        public async Task InputCheck_WhenBoardNotConnected_RaisesSequenceError()
        {
            // 현장 재현 — 드라이버 미설치/보드 번호 불일치로 연결 실패한 보드.
            var board = new StubBoard { ThrowOnRead = true };
            var engine = MakeEngine(MakeRegistry(board));

            var config = new SequenceConfig
            {
                Name = "BoardTriggerFail",
                Nodes = new List<SequenceNodeConfig> { Start("s", "wait"), BoardInputCheck("wait", 4, "e"), End("e") }
            };

            string? faultedNodeName = null;
            Exception? faultedError = null;
            engine.SequenceError += (_, e) => { faultedNodeName = e.NodeName; faultedError = e.Error; };

            await engine.RunAsync(config, CancellationToken.None);

            // 조용히 무한 대기하지 않고 오류로 표면화되어야 한다 (AutoProcessService 가 이걸 받아
            // 로그 + 재시도 지연을 건다).
            Assert.Equal("Wait Trigger", faultedNodeName);
            Assert.IsType<InvalidOperationException>(faultedError);
        }

        [Fact]
        public async Task InputCheck_WithUnknownDeviceId_FallsBackToPlcPath()
        {
            // 레지스트리에 없는 DeviceId 는 기본 PLC 경로로 폴백 — 기존 동작 보존.
            var board = new StubBoard();
            var engine = MakeEngine(MakeRegistry(board));

            var node = BoardInputCheck("wait", 4, "e");
            node.DeviceId = "NotRegistered";
            node.PlcAddress = "M100";       // PLC 어드레스로 해석되어야 함
            _plc.SimSetBit("M100", true);

            var config = new SequenceConfig
            {
                Name = "Fallback",
                Nodes = new List<SequenceNodeConfig> { Start("s", "wait"), node, End("e") }
            };

            SequenceErrorEventArgs? errorNode = null;
            engine.SequenceError += (_, e) => errorNode = e;

            await engine.RunAsync(config, CancellationToken.None);

            Assert.Null(errorNode);
            Assert.Empty(board.ReadChannels);   // 보드는 건드리지 않았다
        }

        [Fact]
        public async Task ToolResultOutput_WriteFailure_RaisesSequenceError_AndWritesOtherMappings()
        {
            // 검사 결과(OK/NG)를 보드로 내보내다 실패하면 — 종전에는 Debug 로그만 남고 운전 화면은 조용했다.
            var board = new StubBoard { FailWriteChannels = { 1 } };
            var result = new VMS.Interfaces.ToolInspectionResult
            {
                ToolName = "Blob 1",
                Success = true,
                PlcMappings =
                {
                    new VMS.Models.PlcResultMapping { ResultKey = "Success", DeviceId = BoardId, PlcAddress = "1" },
                    new VMS.Models.PlcResultMapping { ResultKey = "Success", DeviceId = BoardId, PlcAddress = "2" },
                }
            };
            var engine = MakeEngine(MakeRegistry(board), _ => new[] { result });

            var config = new SequenceConfig
            {
                Name = "ResultOut",
                Nodes = new List<SequenceNodeConfig>
                {
                    Start("s", "insp"),
                    new() { Id = "insp", NodeType = SequenceNodeType.Inspection, Name = "Inspect", CameraId = "Cam1", NextNodeId = "e" },
                    End("e")
                }
            };

            SequenceErrorEventArgs? error = null;
            engine.SequenceError += (_, e) => error = e;

            await engine.RunAsync(config, CancellationToken.None);

            Assert.NotNull(error);
            Assert.Equal("Inspect", error!.NodeName);
            Assert.Contains("검사 결과 출력 실패 1건", error.Error.Message);
            Assert.Contains("Blob 1", error.Error.Message);
            Assert.True(board.GetBit(2));   // 실패한 매핑 뒤의 매핑도 끝까지 썼다
        }

        [Fact]
        public async Task ToolResultOutput_AllWritesSucceed_NoError()
        {
            var board = new StubBoard();
            var result = new VMS.Interfaces.ToolInspectionResult
            {
                ToolName = "Blob 1",
                Success = true,
                PlcMappings = { new VMS.Models.PlcResultMapping { ResultKey = "Success", DeviceId = BoardId, PlcAddress = "3" } }
            };
            var engine = MakeEngine(MakeRegistry(board), _ => new[] { result });

            var config = new SequenceConfig
            {
                Name = "ResultOutOk",
                Nodes = new List<SequenceNodeConfig>
                {
                    Start("s", "insp"),
                    new() { Id = "insp", NodeType = SequenceNodeType.Inspection, Name = "Inspect", CameraId = "Cam1", NextNodeId = "e" },
                    End("e")
                }
            };

            SequenceErrorEventArgs? error = null;
            engine.SequenceError += (_, e) => error = e;

            await engine.RunAsync(config, CancellationToken.None);

            Assert.Null(error);
            Assert.True(board.GetBit(3));
        }

        // ─── 스텁 ────────────────────────────────────────────────

        /// <summary>
        /// 최소 IIoBoardConnection 스텁. ThrowOnRead=true 면 AdLinkDaskConnection 의
        /// EnsureConnected() 와 동일하게 InvalidOperationException 을 던진다.
        /// </summary>
        private sealed class StubBoard : IIoBoardConnection
        {
            private readonly Dictionary<int, bool> _bits = new();

            public List<int> ReadChannels { get; } = new();
            public bool ThrowOnRead { get; init; }

            public string DeviceId => BoardId;
            public IoDeviceType DeviceType => IoDeviceType.AdLinkPci743x;
            public bool IsConnected => !ThrowOnRead;
            public int InputChannelCount => 32;
            public int OutputChannelCount => 32;

            public event EventHandler<IoBitChangedEventArgs>? BitChanged;

            /// <summary>쓰기 시 드라이버 오류를 흉내 낼 채널 (Advantech ErrorCode 실패와 같은 InvalidOperationException).</summary>
            public HashSet<int> FailWriteChannels { get; } = new();

            public void SetBit(int channel, bool value) => _bits[channel] = value;
            public bool GetBit(int channel) => _bits.GetValueOrDefault(channel, false);

            public Task<bool> ConnectAsync() => Task.FromResult(!ThrowOnRead);
            public Task DisconnectAsync() => Task.CompletedTask;

            public Task<bool> ReadBitAsync(int channel)
            {
                if (ThrowOnRead)
                    throw new InvalidOperationException($"StubBoard '{DeviceId}' not connected.");
                ReadChannels.Add(channel);
                return Task.FromResult(_bits.GetValueOrDefault(channel, false));
            }

            public Task<uint> ReadPortAsync(int portNo)
            {
                if (ThrowOnRead)
                    throw new InvalidOperationException($"StubBoard '{DeviceId}' not connected.");
                return Task.FromResult(0u);
            }

            public Task WriteBitAsync(int channel, bool value)
            {
                if (ThrowOnRead)
                    throw new InvalidOperationException($"StubBoard '{DeviceId}' not connected.");
                if (FailWriteChannels.Contains(channel))
                    throw new InvalidOperationException($"DAQNavi WriteBit(ch={channel}) 실패 (ErrorCode ErrorDeviceIoTimeOut)");
                _bits[channel] = value;
                BitChanged?.Invoke(this, new IoBitChangedEventArgs(channel, value));
                return Task.CompletedTask;
            }

            public Task WritePortAsync(int portNo, uint value) => Task.CompletedTask;
            public Task StartMonitoringAsync(int channel, int pollingIntervalMs = 50) => Task.CompletedTask;
            public Task StopMonitoringAsync(int channel) => Task.CompletedTask;
            public Task StopAllMonitoringAsync() => Task.CompletedTask;
            public void Dispose() { }
        }
    }
}
