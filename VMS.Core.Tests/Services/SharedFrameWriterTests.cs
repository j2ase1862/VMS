using System;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.Camera.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// SharedFrameWriter 의 Reader 부재 스킵 검증 — 메인 화면 단독 라이브에서
    /// 매 프레임 MMF 직렬화(더티 페이지 → 페이지파일 상시 flush)가 디스크를
    /// 포화시켜 PC 전체가 멈춘 현장 사고(세연공장 2026-08-29)의 회귀 방어.
    ///
    /// 명명된 커널 객체(MMF/Mutex/Event)를 공유하므로 한 메서드에서 순서대로
    /// 검증한다 (테스트 간 병렬 실행 시 이름 충돌 방지).
    /// </summary>
    public class SharedFrameWriterTests
    {
        [Fact]
        public void WriteFrame_SkipsWithoutReader_WritesWithReader()
        {
            using var writer = new SharedFrameWriter();
            writer.Initialize();

            using var frameA = new Mat(4, 4, MatType.CV_8UC3, new Scalar(10, 20, 30));
            var resultA = new AcquisitionResult { Success = true, Image2D = frameA };

            // ── 1) Reader 없음 → 프레임이 MMF 에 기록되지 않아야 한다 ──
            writer.WriteFrame(resultA);

            var reader = new SharedFrameReader();
            try
            {
                Assert.True(reader.TryConnect());

                // Reader 부재 상태에서 쓴 프레임은 존재하지 않는다 (헤더 미기록)
                Assert.Null(reader.TryReadFrame(skipIfSameFrame: false));

                // ── 2) Reader 연결 후 → 정상 기록·왕복 ──
                writer.ResetReaderProbeCacheForTests();
                writer.WriteFrame(resultA);

                var read = reader.TryReadFrame(skipIfSameFrame: false);
                Assert.NotNull(read);
                Assert.NotNull(read!.Image2D);
                Assert.Equal(4, read.Image2D!.Width);
                Assert.Equal(4, read.Image2D.Height);
                Assert.Equal(1, read.FrameCounter);
                read.Image2D.Dispose();

                // ── 3) Reader 해제 후 → 다시 스킵 (FrameCounter 정지) ──
                reader.Dispose();
                writer.ResetReaderProbeCacheForTests();
                writer.WriteFrame(resultA);

                using var reader2 = new SharedFrameReader();
                Assert.True(reader2.TryConnect());
                var read2 = reader2.TryReadFrame(skipIfSameFrame: false);
                Assert.NotNull(read2);
                Assert.Equal(1, read2!.FrameCounter); // 스킵됨 — 카운터 증가 없음
                read2.Image2D?.Dispose();
            }
            finally
            {
                reader.Dispose();
            }
        }
    }
}
