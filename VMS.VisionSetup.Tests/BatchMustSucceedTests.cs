using System.Collections.Generic;
using System.Linq;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services.BatchTesting;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.Result;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VS-12: 배치 테스트 임계치에서 Must Succeed 를 끈 도구의 실패는 사진 판정에서 뺀다.
    /// Result 도구가 있으면 그 판정은 따른다(그 도구를 연결해 받으면 여전히 FAIL).
    /// </summary>
    public class BatchMustSucceedTests
    {
        private static (BlobTool a, BlobTool b) Tools() =>
            (new BlobTool { Name = "A" }, new BlobTool { Name = "B" });

        private static PassFailCriteria Exempt(VisionToolBase tool)
        {
            var c = new PassFailCriteria();
            c.ToolCriteria[tool.Id] = new ToolPassCriteria { MustSucceed = false };
            return c;
        }

        [Fact]
        public void FailedTool_WithMustSucceedOff_DoesNotFailImage()
        {
            var (a, b) = Tools();
            var pairs = new List<(VisionToolBase, VisionResult)>
            {
                (a, new VisionResult { Success = true }),
                (b, new VisionResult { Success = false, Message = "B 못 찾음" }),
            };

            bool ok = BatchTestRunner.EvaluateExecution(pairs, pairs.Select(p => p.Item2).ToList(),
                Exempt(b), lastRunSuccess: false, out var reason);

            Assert.True(ok);
            Assert.Null(reason);
        }

        [Fact]
        public void FailedTool_WithMustSucceedOn_FailsImage()
        {
            var (a, b) = Tools();
            var pairs = new List<(VisionToolBase, VisionResult)>
            {
                (a, new VisionResult { Success = false, Message = "A 못 찾음" }),
                (b, new VisionResult { Success = true }),
            };

            bool ok = BatchTestRunner.EvaluateExecution(pairs, pairs.Select(p => p.Item2).ToList(),
                Exempt(b), lastRunSuccess: false, out var reason);

            Assert.False(ok);
            Assert.Equal("A 못 찾음", reason);
        }

        [Fact]
        public void ResultToolJudgment_StillApplies()
        {
            var (a, _) = Tools();
            var result = new ResultTool { Name = "Final" };
            var pairs = new List<(VisionToolBase, VisionResult)>
            {
                (a, new VisionResult { Success = false }),
                (result, new VisionResult { Success = false }),
            };
            var criteria = Exempt(a);

            bool ok = BatchTestRunner.EvaluateExecution(pairs, pairs.Select(p => p.Item2).ToList(),
                criteria, lastRunSuccess: false, out var reason);

            // Result 도구가 A 를 연결해 받아 실패 — Result 의 실패는 면제되지 않는다
            Assert.False(ok);
            Assert.NotNull(reason);
        }
    }
}
