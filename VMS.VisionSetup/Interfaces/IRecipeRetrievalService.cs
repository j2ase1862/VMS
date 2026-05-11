namespace VMS.VisionSetup.Interfaces
{
    /// <summary>
    /// 사용자 요청에 유사한 과거 레시피를 검색해 SLM 컨텍스트용 텍스트로 변환.
    /// 단계 2 (RAG): few-shot 효과로 LLM의 정확도/일관성 향상.
    /// </summary>
    public interface IRecipeRetrievalService
    {
        /// <summary>
        /// 사용자 메시지와 유사한 상위 K개 레시피를 찾아 LLM 친화 텍스트 블록 생성.
        /// 결과가 없으면 null.
        /// </summary>
        /// <param name="userMessage">사용자 자연어 요청</param>
        /// <param name="topK">최대 후보 수(기본 2)</param>
        string? BuildSimilarRecipesContext(string userMessage, int topK = 2);
    }
}
