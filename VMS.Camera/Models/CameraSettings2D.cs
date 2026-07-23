namespace VMS.Camera.Models
{
    /// <summary>
    /// 카메라에서 읽어온 현재 2D 촬영 파라미터 (read-back).
    /// UI 가 "카메라가 실제로 쓰는 값"을 표시할 때 사용 — 앱이 저장한 json 값과
    /// 카메라(예: Mech-Eye Viewer 에서 튜닝한 값)가 다를 수 있다.
    /// </summary>
    /// <param name="ExposureUs">노출 시간 (µs)</param>
    /// <param name="Gain">게인 (dB)</param>
    public record CameraSettings2D(double ExposureUs, double Gain);
}
