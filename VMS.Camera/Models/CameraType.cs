namespace VMS.Camera.Models
{
    /// <summary>
    /// 카메라 스캔 타입
    /// </summary>
    public enum CameraType
    {
        AreaScan2D,
        AreaScan3D,
        LineScan2D,
        LineScan3D
    }

    /// <summary>
    /// 트리거 소스 (Line Scan 카메라용)
    /// </summary>
    public enum TriggerSource
    {
        Internal,
        Encoder
    }

    /// <summary>
    /// 3D 카메라 캡처 모드
    /// </summary>
    public enum CaptureMode3D
    {
        Only2D,
        Only3D,
        Both
    }
}
