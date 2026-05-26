using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VMS.Core.Models.ParameterSync;

namespace VMS.Core.Interfaces
{
    /// <summary>
    /// Web 서버의 ToolParameter(ParamCode/ParamValue)를 동기화하고
    /// 검사 결과를 업로드하는 서비스 인터페이스.
    /// </summary>
    public interface IParameterSyncService : IDisposable
    {
        /// <summary>동기화 완료 이벤트 (success, cachedCount)</summary>
        event Action<bool, int>? SyncCompleted;

        /// <summary>레시피 로드 완료 이벤트 (recipeId, recipeName, paramCount)</summary>
        event Action<int, string, int>? RecipeLoaded;

        /// <summary>Web 레시피 목록이 변경되었을 때 발생</summary>
        event Action<List<RecipeSummaryDto>>? RecipeListChanged;

        /// <summary>Stage 3 — 검사 결과 업로드 후 서버가 갱신한 WO 진행률 스냅샷 (WO 가 첨부된 경우만)</summary>
        event Action<WorkOrderProgressDto>? WorkOrderProgressed;

        /// <summary>Stage 3 — 이 업로드로 WO 가 막 Completed 전이된 경우 발생 (계획 수량 도달)</summary>
        event Action<WorkOrderProgressDto>? WorkOrderCompleted;

        /// <summary>마지막 동기화 시각</summary>
        DateTime? LastSyncedAt { get; }

        /// <summary>현재 로드된 레시피 ID</summary>
        int CurrentRecipeId { get; }

        /// <summary>캐시된 파라미터 수</summary>
        int CachedItemCount { get; }

        /// <summary>C6 — 업로드 실패로 디스크 큐에 보존된 미전송 결과 개수.</summary>
        int PendingUploadCount { get; }

        /// <summary>서버에서 조회된 레시피 목록</summary>
        List<RecipeSummaryDto> Recipes { get; }

        /// <summary>서버에서 레시피 목록을 동기화</summary>
        Task<bool> SyncRecipesAsync();

        /// <summary>특정 레시피의 파라미터를 로드하여 캐시</summary>
        Task<bool> LoadRecipeAsync(int recipeId);

        /// <summary>현재 레시피의 파라미터를 재동기화</summary>
        Task<bool> SyncAsync();

        /// <summary>주기적 동기화 시작</summary>
        void StartPeriodicSync(int intervalSeconds = 60);

        /// <summary>ParamCode로 값을 조회 (캐시 미스 시 defaultValue 반환)</summary>
        double ResolveValue(int paramCode, double defaultValue = 0.0);

        /// <summary>ParamCode 존재 여부 확인</summary>
        bool ValidateCode(int paramCode);

        /// <summary>ParamCode의 존재 및 동기화 상태 확인</summary>
        (bool exists, bool synced) CheckCodeStatus(int paramCode, double currentSetupValue);

        /// <summary>캐시된 모든 파라미터 반환</summary>
        List<RecipeParameterDto> GetAll();

        /// <summary>
        /// 검사 결과를 서버에 업로드.
        /// <paramref name="featureMetrics"/> 가 주어지면 Predictive_DefectRate_Plan §5.1 피처를 동봉.
        /// 기존 호출자(피처 미전달)는 그대로 동작 — 서버는 nullable 컬럼으로 받음.
        /// </summary>
        Task<bool> UploadResultsAsync(
            int recipeId,
            List<ParameterResultDto> results,
            InspectionFeatureMetrics? featureMetrics = null);

        // ─── Phase 3 추적성 컨텍스트 (UploadResultsAsync 호출 시 자동 첨부) ───
        /// <summary>업로드 시 첨부할 작업지시 ID. null이면 미선택.</summary>
        int? WorkOrderId { get; set; }
        /// <summary>업로드 시 첨부할 Lot ID.</summary>
        int? LotId { get; set; }
        /// <summary>업로드 시 첨부할 작업자 ID.</summary>
        int? OperatorId { get; set; }
        /// <summary>업로드 시 첨부할 제품 시리얼 (바코드).</summary>
        string? SerialNumber { get; set; }
    }
}
