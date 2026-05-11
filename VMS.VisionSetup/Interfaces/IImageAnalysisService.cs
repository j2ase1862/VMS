using OpenCvSharp;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Interfaces
{
    /// <summary>
    /// SLM이 ImageDependent 파라미터를 추정할 때 호출하는 OpenCV 기반 분석 함수 집합.
    /// 결과는 LLM에 텍스트로 전달되므로 가볍고 결정적이어야 함.
    /// </summary>
    public interface IImageAnalysisService
    {
        /// <summary>
        /// 픽셀 강도 분포 분석. ThresholdValue, BlobTool.ThresholdValue 추정용.
        /// </summary>
        HistogramAnalysis AnalyzeHistogram(Mat image, Rect? roi = null);

        /// <summary>
        /// 엣지 분포 분석. EdgeThreshold, CannyThreshold, 직선 방향 추정용.
        /// </summary>
        EdgeAnalysis AnalyzeEdges(Mat image, Rect? roi = null);

        /// <summary>
        /// 요청 키워드 리스트("histogram", "edges")에 따라 한 번에 분석.
        /// </summary>
        ImageAnalysisBundle AnalyzeBundle(Mat image, string[] requests, Rect? roi = null);
    }
}
