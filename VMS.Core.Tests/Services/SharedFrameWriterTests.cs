using System;
using OpenCvSharp;
using VMS.Camera.Configuration;
using VMS.Camera.Models;
using VMS.Camera.Services;
using VMS.Core.Tests.Configuration;
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
    // IPC 이름은 AppDataPaths 의 인스턴스 정적 상태에서 파생된다(QualifyIpcName).
    // AppDataPathsTests 가 그 상태를 바꾸는 동안 이 테스트가 병렬로 돌면 Writer 와 Reader 가
    // 서로 다른 이름을 보게 되어 연결이 실패한다 — 같은 컬렉션으로 묶어 직렬화한다.
    //
    // 컬렉션만으로는 부족하다. 기본 인스턴스의 IPC 이름은 접미사가 없어 이 PC 에서 전역이므로,
    // 개발 PC 에 실제 VMS 가 떠 있거나 dotnet test 가 겹쳐 돌면 프로세스 밖에서 이름이 부딪힌다
    // ("다시 돌리면 통과" 하는 간헐 실패). IpcInstanceScope 로 이 실행에만 있는 이름을 씌운다.
    [Collection("AppDataPathsState")]
    public class SharedFrameWriterTests
    {
        [Fact]
        public void WriteFrame_SkipsWithoutReader_WritesWithReader()
        {
            using var ipc = new IpcInstanceScope();
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

        /// <summary>
        /// 프레임에 실린 카메라 식별자가 왕복해야 한다 — 수신 측(VisionSetup)이 "요청한
        /// 카메라의 프레임인지" 대조하는 유일한 근거다. 없으면 다른 카메라 프레임을
        /// 받고도 모른 채 엉뚱한 이미지로 툴을 세팅하게 된다.
        /// 식별자를 싣지 않은 프레임은 빈 문자열("모름")로 읽혀야 하며, 이를 일치로
        /// 오인하지 않도록 수신 측이 구분할 수 있어야 한다.
        /// </summary>
        [Fact]
        public void WriteFrame_RoundTripsCameraId()
        {
            using var ipc = new IpcInstanceScope();
            using var writer = new SharedFrameWriter();
            writer.Initialize();

            using var frame = new Mat(4, 4, MatType.CV_8UC3, new Scalar(1, 2, 3));
            var result = new AcquisitionResult { Success = true, Image2D = frame };

            using var reader = new SharedFrameReader();
            Assert.True(reader.TryConnect());

            // ── 식별자 있음 → 그대로 읽힌다 ──
            writer.ResetReaderProbeCacheForTests();
            writer.WriteFrame(result, "cam-line2");

            var withId = reader.TryReadFrame(skipIfSameFrame: false);
            Assert.NotNull(withId);
            Assert.Equal("cam-line2", withId!.CameraId);
            withId.Image2D?.Dispose();

            // ── 식별자 없음 → 빈 문자열("모름") ──
            writer.ResetReaderProbeCacheForTests();
            writer.WriteFrame(result);

            var withoutId = reader.TryReadFrame(skipIfSameFrame: false);
            Assert.NotNull(withoutId);
            Assert.Equal(string.Empty, withoutId!.CameraId);
            withoutId.Image2D?.Dispose();
        }

        /// <summary>
        /// 점군의 카메라 내부 파라미터가 왕복해야 한다 — VisionSetup 은 카메라를 VMS 에서 넘겨받으므로
        /// 여기서 빠지면 VisionSetup 에서 저장한 .vpc 에 내부 파라미터가 없어 X/Y 를 mm 로 잴 수 없다
        /// (2026-09-23 현장: VMS 저장본에는 있고 VisionSetup 저장본에는 없었다).
        /// 내부 파라미터가 없는 점군은 없는 채로(null) 읽혀야 한다.
        /// </summary>
        [Fact]
        public void WriteFrame_RoundTripsPointCloudIntrinsics()
        {
            using var ipc = new IpcInstanceScope();
            using var writer = new SharedFrameWriter();
            writer.Initialize();

            using var cloud = PointCloudData.FromArrays(
                new float[] { 10, 20, 1800, 11, 20, 1801, 10, 21, 0, 11, 21, 1802 }, width: 2, height: 2);
            cloud.Intrinsics = new DepthIntrinsics { Fx = 1795.89, Fy = 1795.89, Cx = 943.71, Cy = 766.75 };

            using var reader = new SharedFrameReader();
            Assert.True(reader.TryConnect());

            writer.ResetReaderProbeCacheForTests();
            writer.WriteFrame(new AcquisitionResult { Success = true, PointCloud = cloud }, "cam-3d");
            var read = reader.TryReadFrame(skipIfSameFrame: false);

            Assert.NotNull(read?.PointCloud);
            var pc = read!.PointCloud!;
            Assert.Equal(4, pc.PointCount);
            Assert.Equal(2, pc.GridWidth);
            Assert.Equal(1801f, pc.Positions[1].Z);
            Assert.NotNull(pc.Intrinsics);
            Assert.Equal(1795.89, pc.Intrinsics!.Fx);
            Assert.Equal(1795.89, pc.Intrinsics.Fy);
            Assert.Equal(943.71, pc.Intrinsics.Cx);
            Assert.Equal(766.75, pc.Intrinsics.Cy);
            pc.Dispose();

            // 내부 파라미터 없는 점군 → null 로 읽힌다
            cloud.Intrinsics = null;
            writer.ResetReaderProbeCacheForTests();
            writer.WriteFrame(new AcquisitionResult { Success = true, PointCloud = cloud }, "cam-3d");
            var read2 = reader.TryReadFrame(skipIfSameFrame: false);
            Assert.NotNull(read2?.PointCloud);
            Assert.Null(read2!.PointCloud!.Intrinsics);
            read2.PointCloud.Dispose();
        }

        /// <summary>
        /// 위 두 시험이 기대는 격리 장치 자체를 검증한다.
        ///
        /// <para>IpcInstanceScope 가 실제로 이름을 갈라 주지 않으면, 두 시험은 기본 인스턴스의
        /// 전역 이름(<c>Local\VMS_SharedFrame_*</c>)을 그대로 쓰게 된다 — 개발 PC 에 실제 VMS 가
        /// 떠 있거나 dotnet test 가 겹쳐 돌 때 프로세스 밖에서 부딪히는 그 상태로 조용히 되돌아간다.
        /// 그러면 "다시 돌리면 통과" 하는 간헐 실패가 다시 시작되므로, 갈라짐을 못 박아 둔다.</para>
        /// </summary>
        [Fact]
        public void IpcInstanceScope_gives_each_run_its_own_kernel_object_names()
        {
            AppDataPaths.ResetForTests();
            var shared = SharedFrameConstants.MmfName;

            string inside;
            using (var ipc = new IpcInstanceScope())
            {
                inside = SharedFrameConstants.MmfName;

                Assert.NotEqual(shared, inside);
                Assert.EndsWith("." + ipc.InstanceName, inside);
                // Grab 요청 채널도 같은 규칙을 따라야 한다 (같은 PC 의 다른 실행과 안 부딪히도록)
                Assert.EndsWith("." + ipc.InstanceName, SharedFrameConstants.GrabRequestMmfName);
            }

            // 범위를 벗어나면 원래 이름으로 돌아온다 — 다른 시험에 상태를 흘리지 않는다
            AppDataPaths.ResetForTests();
            Assert.Equal(shared, SharedFrameConstants.MmfName);

            // 범위가 겹치지 않아도 실행마다 이름이 달라야 한다
            using var a = new IpcInstanceScope();
            var first = SharedFrameConstants.MmfName;
            a.Dispose();
            using var b = new IpcInstanceScope();
            Assert.NotEqual(first, SharedFrameConstants.MmfName);
        }

        /// <summary>
        /// Reader 가 둘일 때, 하나가 떠나도 남은 쪽의 프레임 수신은 살아 있어야 한다.
        ///
        /// <para>ReaderAlive 는 이름 있는 커널 객체라 한 프로세스의 모든 Reader 가 같은 것을
        /// 공유한다. 예전 Disconnect 는 여기에 Reset() 을 걸어, 임시 Reader 하나가 사라질 때
        /// "이 PC 의 Reader 가 전부 없어졌다"로 만들었다 — Writer 의 HasReader() 가 false 로
        /// 떨어져 WriteFrame 이 통째로 스킵되고, 살아 있는 메인 화면은 Grab 을 요청해도
        /// 새 프레임을 못 받는다. 캘리브레이션 창의 가용성 프로브가 정확히 이 경로였다
        /// (2026-09-16 현장 보고: 이미지를 한 번에 못 받고 주기적으로 반복).</para>
        /// </summary>
        [Fact]
        public void WriteFrame_KeepsWorking_AfterOneOfTwoReadersDisconnects()
        {
            using var ipc = new IpcInstanceScope();
            using var writer = new SharedFrameWriter();
            writer.Initialize();

            using var frame = new Mat(4, 4, MatType.CV_8UC3, new Scalar(7, 8, 9));
            var result = new AcquisitionResult { Success = true, Image2D = frame };

            using var keeper = new SharedFrameReader();
            Assert.True(keeper.TryConnect());

            // 임시 프로브(캘리브레이션 창의 가용성 확인과 같은 수명)를 붙였다 뗀다
            var probe = new SharedFrameReader();
            Assert.True(probe.TryConnect());
            probe.Dispose();

            writer.ResetReaderProbeCacheForTests();
            writer.WriteFrame(result, "cam-1");

            var read = keeper.TryReadFrame(skipIfSameFrame: false);
            Assert.NotNull(read);
            Assert.Equal(1, read!.FrameCounter);   // 스킵되지 않았다
            Assert.Equal("cam-1", read.CameraId);
            read.Image2D?.Dispose();
        }

        /// <summary>
        /// 같은 Reader 에 TryConnect 를 거듭 불러도 (단발 수신은 Grab 마다 부른다) 동작이
        /// 그대로여야 하고, 이전 핸들을 붙든 채 새 핸들을 얹지 않아야 한다.
        /// </summary>
        [Fact]
        public void TryConnect_IsIdempotent_AndKeepsReaderRegistered()
        {
            using var ipc = new IpcInstanceScope();
            using var writer = new SharedFrameWriter();
            writer.Initialize();

            using var frame = new Mat(4, 4, MatType.CV_8UC3, new Scalar(4, 5, 6));
            var result = new AcquisitionResult { Success = true, Image2D = frame };

            using var reader = new SharedFrameReader();
            for (int i = 0; i < 5; i++)
                Assert.True(reader.TryConnect());

            writer.ResetReaderProbeCacheForTests();
            writer.WriteFrame(result, "cam-1");

            var read = reader.TryReadFrame(skipIfSameFrame: false);
            Assert.NotNull(read);
            Assert.Equal(1, read!.FrameCounter);
            read.Image2D?.Dispose();
        }
    }
}
