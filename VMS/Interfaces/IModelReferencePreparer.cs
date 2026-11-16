using System.Threading.Tasks;

namespace VMS.Interfaces
{
    /// <summary>
    /// 레시피의 <c>model://…</c> 참조 모델을 검사 전에 받아 두는 일 (VM-40).
    ///
    /// <para>
    /// 검사 경로는 네트워크를 쓰지 않고 이미 받아 둔 파일만 쓴다. 그래서 레시피를 열 때 여기서 먼저
    /// 받아 둬야 한다. 받지 못한 참조를 쓰는 도구는 검사에서 "아직 내려받지 못했습니다" 로 불합격하고,
    /// 받기 실패 사유는 System Log 에 남는다 — 운전 자체를 막지는 않는다.
    /// </para>
    /// </summary>
    public interface IModelReferencePreparer
    {
        /// <summary>
        /// 레시피가 가리키는 참조를 준비한다. <paramref name="onlyMissing"/> 가 true 면 아직 받지 못한 참조만 —
        /// 운전 중에 받아 둔 모델 버전을 바꾸지 않는다(Web 동기화 뒤 빈 곳 메우기용).
        /// 예외를 던지지 않는다.
        /// </summary>
        Task PrepareForRecipeAsync(Recipe recipe, bool onlyMissing = false);
    }
}
