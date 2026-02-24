using OpenCvSharp;
using System.Collections.Generic;
using System.Threading.Tasks;
using VMS.Models;

namespace VMS.Interfaces
{
    public class StepInspectionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Mat? OverlayImage { get; set; }
        public double ExecutionTimeMs { get; set; }
        public List<ToolInspectionResult> ToolResults { get; set; } = new();
    }

    public class ToolInspectionResult
    {
        public string ToolName { get; set; } = string.Empty;
        public string ToolType { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public double ExecutionTimeMs { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }

    public interface IInspectionService
    {
        Task<StepInspectionResult> ExecuteStepAsync(InspectionStep step, Mat inputImage);

        /// <summary>
        /// 레시피 변경 시 캐싱된 도구 인스턴스를 초기화
        /// </summary>
        void ClearCache();
    }
}
