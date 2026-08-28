namespace VMS.Camera.Models
{
    /// <summary>
    /// Grab 요청 처리 결과. 거절 사유를 코드로 구분해 요청 측이 사람이 읽을 메시지를
    /// 만들 수 있게 한다 — 타임아웃 하나로 뭉뚱그리면 "왜 안 되는지" 알 수 없다.
    /// </summary>
    public enum GrabRequestStatus
    {
        /// <summary>요청이 기록되었고 아직 처리되지 않음.</summary>
        Pending = 0,

        /// <summary>Grab 성공 — 프레임이 공유 메모리에 기록됨.</summary>
        Success = 1,

        /// <summary>운전(AUTO RUN) 중이라 거절.</summary>
        RejectedAutoRun = 2,

        /// <summary>라이브 구동 중이라 거절.</summary>
        RejectedLive = 3,

        /// <summary>요청한 카메라를 찾을 수 없음.</summary>
        CameraNotFound = 4,

        /// <summary>Grab 을 시도했으나 실패 (카메라 오류 등).</summary>
        GrabFailed = 5,
    }

    /// <summary>VisionSetup → VMS Grab 요청.</summary>
    public sealed class GrabRequest
    {
        /// <summary>요청 식별자 — 응답이 이 요청에 대한 것인지 대조용.</summary>
        public long RequestId { get; set; }

        /// <summary>Grab 대상 카메라 (CameraInfo.Id). 비우면 VMS 의 현재 선택 카메라.</summary>
        public string CameraId { get; set; } = string.Empty;
    }

    /// <summary>VMS → VisionSetup Grab 응답.</summary>
    public sealed class GrabResponse
    {
        public long RequestId { get; set; }
        public GrabRequestStatus Status { get; set; }

        /// <summary>실제로 Grab 한 카메라 (요청이 비어 있었을 때 어느 카메라였는지 알려준다).</summary>
        public string CameraId { get; set; } = string.Empty;

        /// <summary>성공 시 기록된 프레임 번호 — 수신 측이 새 프레임인지 확인하는 데 쓴다.</summary>
        public long FrameCounter { get; set; }

        /// <summary>사람이 읽을 상세 (실패 사유 등).</summary>
        public string Message { get; set; } = string.Empty;

        public bool IsSuccess => Status == GrabRequestStatus.Success;
    }
}
