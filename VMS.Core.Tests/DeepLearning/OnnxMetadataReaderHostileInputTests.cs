using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using VMS.Core.DeepLearning;
using Xunit;

namespace VMS.Core.Tests.DeepLearning
{
    /// <summary>
    /// OnnxMetadataReader 는 신뢰할 수 없는 파일을 읽는다 — VisionSetup 은 현장에서 임의 경로의 ONNX 를 열고,
    /// 레지스트리 캐시 파일도 네트워크를 거쳐 온다. 조작된 길이 varint(2^63 이상)를 (long) 으로 캐스팅하면
    /// 음수가 되어 경계 검사를 통과하고, 그 값으로 Stream.Position 을 되감으면 파싱이 영원히 반복된다.
    /// 보강 전에는 아래 15바이트 이하의 파일 하나로 호출 스레드가 영구 점유됐다 (MLOps OnnxSafeReader 와 같은 결함).
    /// 각 테스트는 파서를 별도 스레드에서 돌려, 멈추더라도 테스트가 매달리지 않고 시간 초과로 실패한다.
    /// </summary>
    public class OnnxMetadataReaderHostileInputTests
    {
        // varint 10바이트 = 2^64 - 11 → (long) 캐스팅 시 -11. 되감긴 위치가 방금 읽은 태그에 떨어지도록 맞춘 값.
        private static readonly byte[] MinusEleven = { 0xF5, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 };

        private static byte[] Concat(params byte[][] parts)
        {
            var list = new List<byte>();
            foreach (var p in parts) list.AddRange(p);
            return list.ToArray();
        }

        /// <summary>graph(7, len=13) → input(11, len=11) → 알 수 없는 필드(2) 길이 = -11 → ReadValueInfoName 의 SkipField 가 되감김</summary>
        private static byte[] GraphInputRewind() =>
            Concat(new byte[] { 0x3A, 0x0D, 0x5A, 0x0B, 0x12 }, MinusEleven);

        /// <summary>metadata_props(14, len=11) → 알 수 없는 필드(3) 길이 = -11 → ParseEntry 의 SkipField 가 되감김</summary>
        private static byte[] MetadataEntryRewind() =>
            Concat(new byte[] { 0x72, 0x0B, 0x1A }, MinusEleven);

        /// <summary>최상위 producer_name(2) 길이 = -11 → 최상위 SkipField 가 파일 처음으로 되감김</summary>
        private static byte[] TopLevelRewind() =>
            Concat(new byte[] { 0x12 }, MinusEleven);

        [Fact]
        public void ReadGraphIoNames_oversized_length_inside_graph_returns_promptly()
        {
            var (inputs, outputs) = WithinTimeout(GraphInputRewind(), p => OnnxMetadataReader.ReadGraphIoNames(p));
            Assert.Empty(inputs);
            Assert.Empty(outputs);
        }

        [Fact]
        public void Read_oversized_length_inside_metadata_entry_returns_promptly()
        {
            var meta = WithinTimeout(MetadataEntryRewind(), p => OnnxMetadataReader.Read(p));
            Assert.Empty(meta);
        }

        [Fact]
        public void Both_readers_survive_top_level_rewind()
        {
            var meta = WithinTimeout(TopLevelRewind(), p => OnnxMetadataReader.Read(p));
            Assert.Empty(meta);
            var (inputs, outputs) = WithinTimeout(TopLevelRewind(), p => OnnxMetadataReader.ReadGraphIoNames(p));
            Assert.Empty(inputs);
            Assert.Empty(outputs);
        }

        [Fact]
        public void Probe_on_hostile_file_falls_back_without_hanging()
        {
            // Probe 는 Read → ReadGraphIoNames 순으로 둘 다 부른다. 판별 실패 시 YOLO 폴백이 규약.
            var format = WithinTimeout(GraphInputRewind(), p => DetectionModelFormatProbe.Probe(p));
            Assert.Equal(DetectionModelFormat.Yolo, format);
        }

        /// <summary>잘린 파일·쓰레기 바이트·파일보다 큰(양수) 길이로도 멈추거나 예외가 새지 않아야 한다</summary>
        [Theory]
        [InlineData(new byte[] { 0x3A })]                                                                   // graph 태그만
        [InlineData(new byte[] { 0x3A, 0xFF, 0x7F })]                                                       // graph 길이 16383 > 파일
        [InlineData(new byte[] { 0x72, 0xFF, 0x7F })]                                                       // metadata 길이 16383 > 파일
        [InlineData(new byte[] { 0x3A, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]       // 10바이트 varint 초과
        [InlineData(new byte[] { 0x0A, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 })] // 11바이트 varint (불법)
        [InlineData(new byte[] { 0x3A, 0x04, 0x5A, 0x02, 0x0A, 0xFF })]                                     // name 길이가 ValueInfo 를 넘음
        [InlineData(new byte[] { 0x72, 0x08, 0x0A, 0xFF, 0xFF, 0xFF, 0x0F, 0x12, 0x01, 0x61 })]             // key 길이 2^28-1 > entry
        [InlineData(new byte[] { 0x3B, 0x00 })]                                                             // wire type 3 (SGROUP) — ONNX 미사용
        [InlineData(new byte[] { 0x09, 0x01, 0x02 })]                                                       // fixed64 인데 2바이트뿐
        public void Malformed_files_return_promptly(byte[] bytes)
        {
            var meta = WithinTimeout(bytes, p => OnnxMetadataReader.Read(p));
            Assert.NotNull(meta);
            var io = WithinTimeout(bytes, p => OnnxMetadataReader.ReadGraphIoNames(p));
            Assert.NotNull(io.Inputs);
            Assert.NotNull(io.Outputs);
        }

        /// <summary>
        /// 파서를 백그라운드 스레드에서 돌리고 제한 시간 안에 끝나는지 본다.
        /// 멈추면 그 스레드는 버려지고(IsBackground) 테스트는 실패 메시지와 함께 끝난다.
        /// </summary>
        private static T WithinTimeout<T>(byte[] bytes, Func<string, T> parse, int timeoutMs = 5000)
        {
            var path = Path.Combine(Path.GetTempPath(), $"vms-core-hostile-{Guid.NewGuid():N}.onnx");
            File.WriteAllBytes(path, bytes);
            try
            {
                T result = default!;
                Exception? error = null;
                var thread = new Thread(() =>
                {
                    try { result = parse(path); }
                    catch (Exception ex) { error = ex; }
                }) { IsBackground = true, Name = "onnx-hostile-parse" };
                thread.Start();

                if (!thread.Join(timeoutMs))
                    throw new Xunit.Sdk.XunitException(
                        $"파서가 {timeoutMs}ms 안에 끝나지 않았다 — 조작된 길이로 스트림이 되감겨 무한 반복 중 ({bytes.Length}바이트 입력)");
                if (error != null)
                    throw new Xunit.Sdk.XunitException($"파서가 예외를 밖으로 냈다: {error}");
                return result;
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
