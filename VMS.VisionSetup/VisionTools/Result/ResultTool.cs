using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.Result
{
    public enum ResultJudgmentMode
    {
        AllPass,
        AnyPass
    }

    public class SourceToolResult
    {
        public string ToolId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class ResultTool : VisionToolBase
    {
        private ResultJudgmentMode _judgmentMode = ResultJudgmentMode.AllPass;
        public ResultJudgmentMode JudgmentMode
        {
            get => _judgmentMode;
            set => SetProperty(ref _judgmentMode, value);
        }

        /// <summary>
        /// VisionService가 Execute 호출 전에 채워주는 소스 결과 목록
        /// </summary>
        public List<SourceToolResult> SourceResults { get; } = new();

        public ResultTool()
        {
            Name = "Result";
            ToolType = "ResultTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var sw = Stopwatch.StartNew();
            var result = new VisionResult();

            int pass = SourceResults.Count(r => r.Success);
            int fail = SourceResults.Count - pass;
            int total = SourceResults.Count;

            bool ok = JudgmentMode switch
            {
                ResultJudgmentMode.AllPass => total > 0 && fail == 0,
                ResultJudgmentMode.AnyPass => pass > 0,
                _ => total > 0 && fail == 0
            };

            result.Success = ok;
            result.Message = ok
                ? $"OK ({pass}/{total} passed)"
                : $"NG ({fail}/{total} failed)";

            result.Data["Success"] = ok;
            result.Data["PassCount"] = pass;
            result.Data["FailCount"] = fail;
            result.Data["TotalCount"] = total;

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;

            return result;
        }

        public override List<string> GetAvailableResultKeys()
        {
            // Success는 시퀀스 에디터의 Branch→OutputAction에서 담당하므로 제외
            return new List<string>
            {
                "PassCount",
                "FailCount",
                "TotalCount"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new ResultTool
            {
                Name = Name,
                ToolType = ToolType,
                IsEnabled = IsEnabled,
                JudgmentMode = JudgmentMode
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
