using System.Threading.Tasks;
using VMS.Camera.Models;
using VMS.Camera.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// VisionSetup → VMS Grab 요청 처리 규칙.
    ///
    /// 핵심은 "거절을 사유와 함께 돌려준다"는 것 — 운전(AUTO RUN)·라이브 중에 카메라를
    /// 뺏으면 검사 사이클이 깨지므로 거절해야 하고, 요청 측이 이유를 모른 채 타임아웃까지
    /// 기다리면 안 된다. 커널 객체를 만들지 않는 HandleAsync 만 직접 검증한다.
    /// </summary>
    public class GrabRequestListenerTests
    {
        private static GrabRequestListener.GrabRequestContext Context(
            bool autoRun = false,
            bool live = false,
            string defaultCameraId = "cam-1",
            bool grabSucceeds = true,
            string? existingCameraId = "cam-1",
            long frameCounter = 42)
            => new()
            {
                IsAutoRunning = autoRun,
                IsLiveMode = live,
                DefaultCameraId = defaultCameraId,
                CameraExists = id => existingCameraId != null && id == existingCameraId,
                CurrentFrameCounter = () => frameCounter,
                GrabAsync = _ => Task.FromResult(grabSucceeds)
            };

        private static Task<GrabResponse> Handle(
            GrabRequestListener.GrabRequestContext ctx, string cameraId = "cam-1", long requestId = 7)
        {
            var listener = new GrabRequestListener(() => ctx);
            return listener.HandleAsync(new GrabRequest { RequestId = requestId, CameraId = cameraId });
        }

        [Fact]
        public async Task AutoRun_IsRejected_WithReason()
        {
            var r = await Handle(Context(autoRun: true));

            Assert.Equal(GrabRequestStatus.RejectedAutoRun, r.Status);
            Assert.False(r.IsSuccess);
            Assert.Contains("AUTO RUN", r.Message);
            Assert.Equal(7, r.RequestId);   // 응답이 이 요청의 것임을 요청 측이 대조한다
        }

        [Fact]
        public async Task LiveMode_IsRejected_WithReason()
        {
            var r = await Handle(Context(live: true));

            Assert.Equal(GrabRequestStatus.RejectedLive, r.Status);
            Assert.Contains("라이브", r.Message);
        }

        [Fact]
        public async Task UnknownCamera_IsRejected()
        {
            var r = await Handle(Context(existingCameraId: "other-cam"), cameraId: "cam-1");

            Assert.Equal(GrabRequestStatus.CameraNotFound, r.Status);
        }

        [Fact]
        public async Task NoCameraAtAll_IsRejected()
        {
            // 요청도 비어 있고 VMS 에 기본 카메라도 없는 경우
            var r = await Handle(
                Context(defaultCameraId: string.Empty, existingCameraId: null), cameraId: string.Empty);

            Assert.Equal(GrabRequestStatus.CameraNotFound, r.Status);
        }

        [Fact]
        public async Task EmptyCameraId_FallsBackToDefaultCamera()
        {
            // 요청이 카메라를 지정하지 않으면 VMS 의 기본 카메라를 쓰고,
            // 어느 카메라였는지 응답에 실어 돌려준다
            var r = await Handle(Context(defaultCameraId: "cam-9", existingCameraId: "cam-9"),
                cameraId: string.Empty);

            Assert.True(r.IsSuccess);
            Assert.Equal("cam-9", r.CameraId);
        }

        [Fact]
        public async Task Success_ReportsCameraAndFrameCounter()
        {
            var r = await Handle(Context(frameCounter: 128));

            Assert.Equal(GrabRequestStatus.Success, r.Status);
            Assert.True(r.IsSuccess);
            Assert.Equal("cam-1", r.CameraId);
            Assert.Equal(128, r.FrameCounter);
        }

        [Fact]
        public async Task GrabFailure_IsReportedNotSilentlySucceeded()
        {
            var r = await Handle(Context(grabSucceeds: false));

            Assert.Equal(GrabRequestStatus.GrabFailed, r.Status);
            Assert.False(r.IsSuccess);
            Assert.NotEmpty(r.Message);
        }

        [Fact]
        public async Task AutoRun_TakesPrecedence_OverMissingCamera()
        {
            // 운전 중이면 카메라 유무를 따지기 전에 거절 — 카메라를 건드리지 않는다
            var r = await Handle(Context(autoRun: true, existingCameraId: null));

            Assert.Equal(GrabRequestStatus.RejectedAutoRun, r.Status);
        }
    }
}
