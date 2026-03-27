using OpenCvSharp;
using System.Collections.Generic;

namespace VMS.VisionSetup.VisionTools.CodeReading
{
    /// <summary>
    /// 코드 리더 인터페이스 (향후 OpenCV/상용 라이브러리로 교체 가능)
    /// </summary>
    public interface ICodeReader
    {
        List<CodeResult> Read(Mat image, CodeReaderMode mode, bool tryHarder = true);
    }

    /// <summary>
    /// 코드 인식 결과
    /// </summary>
    public class CodeResult
    {
        /// <summary>디코딩된 문자열</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>코드 포맷 ("QR_CODE", "DATA_MATRIX", "CODE_128" 등)</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>코드 꼭짓점 좌표 (4점)</summary>
        public Point2f[] Points { get; set; } = System.Array.Empty<Point2f>();
    }

    /// <summary>
    /// 코드 리더 모드
    /// </summary>
    public enum CodeReaderMode
    {
        /// <summary>자동 인식 (모든 코드 타입)</summary>
        Auto,
        /// <summary>QR 코드 전용</summary>
        QRCode,
        /// <summary>1D 바코드 전용</summary>
        Barcode1D,
        /// <summary>DataMatrix 전용</summary>
        DataMatrix,
        /// <summary>PDF417 전용</summary>
        PDF417
    }
}
