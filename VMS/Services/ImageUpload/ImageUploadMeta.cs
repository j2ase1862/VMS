namespace VMS.Services.ImageUpload
{
    /// <summary>
    /// Web 이미지 업로드의 메타 — multipart 의 "meta"(JSON) part 로 전송되고,
    /// 로컬 큐 사이드카(.json)로도 직렬화되어 재시도 시 복원된다.
    /// Web 은 CorrelationKey 로 InspectionHistory 행과 지연 매칭한다.
    /// </summary>
    public sealed class ImageUploadMeta
    {
        public int ClientIndex { get; set; }
        public string CorrelationKey { get; set; } = string.Empty;
        public string Verdict { get; set; } = string.Empty;   // OK | NG
        public string Variant { get; set; } = string.Empty;   // full | thumb
        public string Ext { get; set; } = "jpg";              // 전송 파일 확장자(png/jpg/...)
        public string CapturedAt { get; set; } = string.Empty; // ISO 8601
        public string CameraName { get; set; } = string.Empty;
        public int Step { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string WorkOrder { get; set; } = string.Empty;
        public string Lot { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;

        // 큐 내부 운영 필드(서버 전송 안 함) — 재시도 백오프.
        public int Attempts { get; set; }
        public string? NextAttemptAtUtc { get; set; }  // ISO 8601 UTC; null 이면 즉시
    }
}
