using OpenCvSharp;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// RollerInspectionService 누적 상한 검증 — 밝기 임계값 오설정 등으로 장면이
    /// 계속 밝으면 풀해상도 클론이 상한 없이 쌓여 메모리 고갈 → 페이지파일
    /// 스래싱으로 번진다. 상한 도달 시 강제 조합 + 쿨다운 전환을 보장한다.
    /// </summary>
    public class RollerInspectionServiceTests
    {
        private static Mat BrightFrame() => new(8, 8, MatType.CV_8UC1, new Scalar(255));
        private static Mat DarkFrame() => new(8, 8, MatType.CV_8UC1, new Scalar(0));

        [Fact]
        public void ProcessFrame_ForcesCompletionAtFrameCap_WhenSceneStaysBright()
        {
            var svc = new RollerInspectionService();
            svc.Start();

            int captured = 0;
            int capturedFrameCount = 0;
            bool exited = false;
            svc.PaperCaptured += r => { captured++; capturedFrameCount = r.FrameCount; };
            svc.PaperExited += () => exited = true;

            // 진입 확정(3) + 상한(500) + 여유(10) — 어두운 프레임 없이 계속 밝음
            for (int i = 0; i < 513 && captured == 0; i++)
            {
                using var f = BrightFrame();
                svc.ProcessFrame(f);
            }

            Assert.Equal(1, captured);
            Assert.True(exited);
            Assert.True(capturedFrameCount <= 500,
                $"누적 상한(500)을 초과: {capturedFrameCount}");

            svc.Stop();
        }

        [Fact]
        public void ProcessFrame_NormalCapture_CompletesOnDarkExit()
        {
            var svc = new RollerInspectionService();
            svc.Start();

            int captured = 0;
            svc.PaperCaptured += _ => captured++;

            for (int i = 0; i < 10; i++)
            {
                using var f = BrightFrame();
                svc.ProcessFrame(f);
            }
            for (int i = 0; i < 5; i++)
            {
                using var f = DarkFrame();
                svc.ProcessFrame(f);
            }

            Assert.Equal(1, captured);
            svc.Stop();
        }
    }
}
