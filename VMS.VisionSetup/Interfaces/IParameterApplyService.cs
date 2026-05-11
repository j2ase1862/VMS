using System.Collections.Generic;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Interfaces
{
    /// <summary>
    /// 외부 입력(Web 동기화/SLM 추정 등)으로 들어온 파라미터 값을 비전 도구 프로퍼티에 적용하는 서비스.
    /// </summary>
    public interface IParameterApplyService
    {
        /// <summary>
        /// 도구의 LinkedParamCodes에 매핑된 Web 파라미터 값을 도구 프로퍼티에 적용.
        /// </summary>
        /// <param name="tool">파라미터를 적용할 비전 도구</param>
        /// <returns>적용된 파라미터 수</returns>
        int ApplyParameters(VisionToolBase tool);

        /// <summary>
        /// 외부 소스(SLM, JSON, 사용자 입력 등)에서 받은 파라미터 dictionary를 도구에 적용.
        /// enum, bool, 정수, 실수, 문자열 타입을 자동 변환. JsonElement도 처리.
        /// </summary>
        /// <param name="tool">대상 도구</param>
        /// <param name="parameters">PropertyName -> Value 매핑</param>
        /// <returns>적용 개수와 실패 사유 목록</returns>
        (int applied, List<string> warnings) ApplyParameters(
            VisionToolBase tool, IReadOnlyDictionary<string, object?> parameters);
    }
}
