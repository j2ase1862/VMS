using System.Collections.Generic;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>Feature Match 재학습 → 연결된 Match Align 기준 영향 안내 문구 (2026-09-04).</summary>
    public class MatchAlignRetrainAdvisorTests
    {
        private static (string, string, ConnectionType) Link(VisionToolBase s, VisionToolBase t, ConnectionType type = ConnectionType.Result)
            => (s.Id, t.Id, type);

        [Fact]
        public void NoConnectedAlign_ReturnsNothing()
        {
            var fm = new FeatureMatchTool { Name = "FM" };
            var other = new MatchAlignTool { Name = "MA" };
            var (note, dialog) = MatchAlignRetrainAdvisor.Advise(fm, false,
                new[] { Link(fm, other, ConnectionType.Image) }, new VisionToolBase[] { fm, other });
            Assert.Null(note);
            Assert.Null(dialog);
        }

        [Fact]
        public void TrainedReferenceAlign_StatusOnly_ReflectsOriginMode()
        {
            var fm = new FeatureMatchTool { Name = "FM" };
            var ma = new MatchAlignTool { Name = "MA", UseTrainedReference = true };
            var tools = new VisionToolBase[] { fm, ma };

            var (updated, d1) = MatchAlignRetrainAdvisor.Advise(fm, false, new[] { Link(fm, ma) }, tools);
            Assert.Contains("갱신", updated);
            Assert.Contains("MA", updated);
            Assert.Null(d1);

            var (kept, d2) = MatchAlignRetrainAdvisor.Advise(fm, true, new[] { Link(fm, ma) }, tools);
            Assert.Contains("유지", kept);
            Assert.Null(d2);
        }

        [Fact]
        public void ManualReferenceAlign_RaisesWarningDialog()
        {
            var fm = new FeatureMatchTool { Name = "FM" };
            var manual = new MatchAlignTool { Name = "MA-manual", UseTrainedReference = false };
            var trained = new MatchAlignTool { Name = "MA-trained", UseTrainedReference = true };
            var tools = new VisionToolBase[] { fm, manual, trained };

            var (note, dialog) = MatchAlignRetrainAdvisor.Advise(fm, false,
                new List<(string, string, ConnectionType)> { Link(fm, manual), Link(fm, trained) }, tools);

            Assert.NotNull(note);
            Assert.Contains("MA-manual", note);
            Assert.Contains("재등록", note);
            Assert.NotNull(dialog);
            Assert.Contains("MA-manual", dialog);
            Assert.Contains("현재 매칭을 기준으로 등록", dialog);
            Assert.DoesNotContain("MA-trained", dialog);
        }
    }
}
