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
        /// <summary>이 검사의 상관 키 — 결과 업로드와 이미지 업로드가 공유(Web 매칭용).</summary>
        public string? CorrelationKey { get; set; }
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

        // PLC result output mappings (1:N)
        public List<Models.PlcResultMapping> PlcMappings { get; set; } = new();
    }

    public interface IInspectionService
    {
        Task<StepInspectionResult> ExecuteStepAsync(InspectionStep step, Mat inputImage);

        /// <summary>
        /// 현재 레시피를 실행 엔진에 알린다 (레시피 전환 시마다 호출).
        /// 스텝 밖에 있는 실행 컨텍스트 — 지금은 캘리브레이션 — 를 공급하는 용도.
        /// 이게 없으면 mm 판정 도구가 "변환 불가"로 무조건 NG 가 된다.
        /// </summary>
        void SetRecipeContext(Recipe? recipe);

        /// <summary>
        /// 레시피 변경 시 캐싱된 도구 인스턴스를 초기화
        /// </summary>
        void ClearCache();
    }
}
