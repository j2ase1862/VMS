using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Interfaces
{
    /// <summary>
    /// VisionToolBase의 LinkedParamCodes를 읽어 Web 파라미터 값을 도구 프로퍼티에 적용하는 서비스.
    /// </summary>
    public interface IParameterApplyService
    {
        /// <summary>
        /// 도구의 LinkedParamCodes에 매핑된 Web 파라미터 값을 도구 프로퍼티에 적용.
        /// </summary>
        /// <param name="tool">파라미터를 적용할 비전 도구</param>
        /// <returns>적용된 파라미터 수</returns>
        int ApplyParameters(VisionToolBase tool);
    }
}
