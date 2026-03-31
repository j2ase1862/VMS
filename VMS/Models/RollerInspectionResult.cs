using System;
using System.Windows.Media.Imaging;

namespace VMS.Models
{
    /// <summary>
    /// 롤러 검사에서 감지된 용지 캡처 결과
    /// </summary>
    public class RollerInspectionResult
    {
        /// <summary>
        /// 캡처 시각
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 누적/조합된 용지 전체 이미지 (BitmapSource, UI 표시용)
        /// </summary>
        public BitmapSource? PaperImage { get; set; }

        /// <summary>
        /// 누적된 프레임 수
        /// </summary>
        public int FrameCount { get; set; }

        /// <summary>
        /// 감지에서 캡처 완료까지 소요 시간 (ms)
        /// </summary>
        public double CaptureTimeMs { get; set; }
    }
}
