using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// Cognex ViDi Red Analyze 대응 — ONNX 기반 이상 탐지 도구.
    /// PatchCore, FastFlow, EfficientAD 등 anomaly detection 모델의 ONNX를 로드하여
    /// 정상/이상 판정 및 히트맵을 생성합니다.
    ///
    /// 모델 출력 형태:
    ///   - anomaly_score: [1] or [1,1] — 전체 이미지 이상 점수
    ///   - anomaly_map: [1,1,H,W] — 픽셀별 이상 히트맵 (선택)
    /// </summary>
    public partial class AnomalyTool : VisionToolBase
    {
        private AnomalyOnnxEngine? _engine;
        private readonly object _engineLock = new();

        // ── Parameters ──

        [ObservableProperty]
        private string _modelPath = string.Empty;

        [ObservableProperty]
        private int _inputSize = 224;

        [ObservableProperty]
        private double _anomalyThreshold = 0.5;

        [ObservableProperty]
        private bool _drawOverlay = true;

        [ObservableProperty]
        private bool _showHeatmap = true;

        [ObservableProperty]
        private double _heatmapOpacity = 0.4;

        // ── 자동 임계값 캘리브레이션 ──

        /// <summary>캘리브레이션용 정상 이미지 폴더</summary>
        [ObservableProperty]
        private string _calibrationFolder = string.Empty;

        /// <summary>threshold = mean + k·std 에서의 k 값 (기본 3σ = 99.7% 분위, 보수적: 0.27% 오검)</summary>
        [ObservableProperty]
        private double _calibrationSigma = 3.0;

        /// <summary>마지막 캘리브레이션 결과 메시지 (UI 표시용)</summary>
        [ObservableProperty]
        private string _calibrationReport = string.Empty;

        public AnomalyTool()
        {
            Name = "Anomaly";
            ToolType = "AnomalyTool";
        }

        /// <summary>
        /// CalibrationFolder 내 이미지들을 순차 추론하여 AnomalyScore 분포를 구한 뒤,
        /// mean + sigma·std 값으로 AnomalyThreshold를 자동 설정합니다.
        /// 정상 이미지만 포함된 폴더를 지정하세요.
        /// UseROI 면 Execute 와 같은 ROI 로 잘라 채점한다 — 원본 전체로 채점하면 검사 점수와 눈금이 달라
        /// 임계값이 어긋난다. 그래서 폴더에는 검사와 같은 화각 · 크기의 원본 이미지를 넣어야 한다.
        /// </summary>
        public async Task<bool> CalibrateThresholdAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(CalibrationFolder) || !Directory.Exists(CalibrationFolder))
            {
                CalibrationReport = "캘리브레이션 폴더가 유효하지 않습니다.";
                return false;
            }
            if (string.IsNullOrEmpty(ModelPath))
            {
                CalibrationReport = "모델 경로를 먼저 지정하세요.";
                return false;
            }

            var exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif" };
            var files = Directory
                .EnumerateFiles(CalibrationFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (files.Count < 3)
            {
                CalibrationReport = $"이미지가 부족합니다 ({files.Count}장). 3장 이상 권장.";
                return false;
            }

            var calibEngine = EnsureEngine();
            bool useRoi = UseROI;
            // Fixture 로 옮겨진 뒤라면 ROI 는 마지막 실행 위치에 머물러 있다 — 기준 자세 ROI 로 채점한다
            var calibRoi = HasFixtureBaseROI ? FixtureBaseROI : ROI;
            int roiW = Math.Abs(calibRoi.Width), roiH = Math.Abs(calibRoi.Height);

            var scores = new List<double>(files.Count);
            int roiOutside = 0;   // ROI 가 이미지와 겹치지 않아 건너뛴 장
            int roiClipped = 0;   // ROI 일부가 이미지 밖이라 잘린 장 (Execute 와 같이 겹친 부분만 채점)
            await Task.Run(() =>
            {
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var img = Cv2.ImRead(f, ImreadModes.Color);
                        if (img.Empty()) continue;

                        Mat? crop = null;
                        if (useRoi)
                        {
                            // Execute 의 GetROIImage 와 같은 자르기 (겹침이 없으면 그쪽은 원본 전체를 쓰지만 여기선 건너뛴다)
                            var adj = ClipRectToImage(calibRoi, img);
                            if (adj.Width <= 0 || adj.Height <= 0) { roiOutside++; continue; }
                            if (adj.Width < roiW || adj.Height < roiH) roiClipped++;
                            crop = new Mat(img, adj);
                        }

                        using (crop)
                        {
                            var r = calibEngine.Detect(crop ?? img, InputSize);
                            scores.Add(r.AnomalyScore);
                            r.AnomalyMap?.Dispose();
                        }
                    }
                    catch { /* 개별 실패는 무시하고 계속 */ }
                }
            }, ct);

            string roiNote = useRoi
                ? $"ROI ({Math.Min(calibRoi.X, calibRoi.X + calibRoi.Width)},{Math.Min(calibRoi.Y, calibRoi.Y + calibRoi.Height)} {roiW}×{roiH}) 영역으로 채점"
                : "이미지 전체로 채점";

            if (scores.Count == 0)
            {
                CalibrationReport = roiOutside > 0
                    ? $"ROI 가 캘리브레이션 이미지 밖에 있습니다 ({roiOutside}장). " +
                      "검사와 같은 화각 · 크기의 원본 이미지를 넣으세요."
                    : "유효한 이미지에서 점수를 얻지 못했습니다.";
                return false;
            }

            double mean = scores.Average();
            double variance = scores.Sum(s => (s - mean) * (s - mean)) / scores.Count;
            double std = Math.Sqrt(variance);
            double suggested = mean + CalibrationSigma * std;

            AnomalyThreshold = Math.Clamp(suggested, 0.0, 1.0);
            CalibrationReport =
                $"샘플 {scores.Count}장 · {roiNote} · mean={mean:F3} · std={std:F3} · " +
                $"suggested={suggested:F3} (→ 적용: {AnomalyThreshold:F3})";
            if (roiOutside > 0)
                CalibrationReport += $"{Environment.NewLine}⚠ ROI 가 이미지 밖이라 건너뜀 {roiOutside}장 — 검사와 같은 크기의 원본인지 확인하세요.";
            if (roiClipped > 0)
                CalibrationReport += $"{Environment.NewLine}⚠ ROI 일부가 이미지 밖 {roiClipped}장 — 겹친 부분만 채점했습니다.";
            return true;
        }

        partial void OnModelPathChanged(string value)
        {
            // 캐시가 엔진 생명주기를 관리한다 — 이 도구는 Dispose하지 않는다.
            lock (_engineLock)
            {
                _engine = null;
            }
            OnnxEngineCache.PrefetchAnomaly(value, InputSize);
        }

        private AnomalyOnnxEngine EnsureEngine()
        {
            lock (_engineLock)
            {
                if (_engine != null) return _engine;
            }

            var engine = OnnxEngineCache.GetAnomaly(ModelPath, InputSize);

            lock (_engineLock)
            {
                _engine = engine;
                return engine;
            }
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            Mat? roiImage = null;
            AnomalyResult? anomalyResult = null;

            try
            {
                if (string.IsNullOrEmpty(ModelPath))
                {
                    result.Success = false;
                    result.Message = "모델 경로를 지정하세요.";
                    return result;
                }

                // ROI 를 쓸 때만 새 Mat(헤더) — 아래 finally 에서 해제한다
                roiImage = UseROI ? GetROIImage(inputImage) : null;

                var engine = EnsureEngine();
                anomalyResult = engine.Detect(roiImage ?? inputImage, InputSize);

                bool isNormal = anomalyResult.AnomalyScore < AnomalyThreshold;

                result.Success = isNormal;
                result.Message = isNormal
                    ? $"정상 (score: {anomalyResult.AnomalyScore:F3})"
                    : $"이상 감지 (score: {anomalyResult.AnomalyScore:F3})";
                result.Data["AnomalyScore"] = anomalyResult.AnomalyScore;
                result.Data["IsNormal"] = isNormal;
                result.Data["Threshold"] = AnomalyThreshold;

                if (DrawOverlay)
                {
                    var overlay = GetColorOverlayBase(inputImage);
                    var color = isNormal ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);

                    // 판정 텍스트
                    string label = isNormal ? "NORMAL" : "ANOMALY";
                    PutHeaderText(overlay, result, $"{label}: {anomalyResult.AnomalyScore:F3}",
                        new Point(10, 30),
                        HersheyFonts.HersheySimplex, 1.0, color, 2);

                    // 히트맵 오버레이 — 맵은 추론한 영역(ROI)의 것이므로 그 영역에만 겹친다.
                    // 종전에는 ROI 맵을 화면 전체 크기로 늘려 덮어, 이상 위치가 엉뚱한 곳에 보였다.
                    if (ShowHeatmap && anomalyResult.AnomalyMap != null)
                    {
                        var mapRect = new Rect(0, 0, overlay.Width, overlay.Height);
                        if (UseROI)
                        {
                            var adj = GetAdjustedROI(inputImage);
                            // 겹침이 없으면 GetROIImage 가 원본 전체로 추론했다 → 전체에 겹친다
                            if (adj.Width > 0 && adj.Height > 0) mapRect = adj;
                        }

                        using var region = new Mat(overlay, mapRect);
                        using var heatmap = new Mat();
                        using var resizedMap = new Mat();

                        Cv2.Resize(anomalyResult.AnomalyMap, resizedMap, mapRect.Size);

                        // 0~1 → 0~255 정규화
                        using var normalized = new Mat();
                        resizedMap.ConvertTo(normalized, MatType.CV_8UC1, 255.0);

                        // 컬러 히트맵 적용
                        Cv2.ApplyColorMap(normalized, heatmap, ColormapTypes.Jet);

                        // 알파 블렌딩 (overlay 의 영역 뷰에 바로 쓴다)
                        Cv2.AddWeighted(region, 1.0 - HeatmapOpacity, heatmap, HeatmapOpacity, 0, region);
                    }

                    // 테두리
                    Cv2.Rectangle(overlay,
                        new Rect(0, 0, overlay.Width, overlay.Height),
                        color, 4);

                    result.OverlayImage = overlay;

                    result.Graphics.Add(new GraphicOverlay
                    {
                        Type = GraphicType.Text,
                        Position = new Point2d(10, 30),
                        Text = $"{label}: {anomalyResult.AnomalyScore:F3}",
                        Color = color
                    });
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Anomaly 오류: {ex.Message}";
            }
            finally
            {
                // 이상 맵은 오버레이에 겹친 뒤 쓰지 않는다 — 종전에는 해제하지 않아 검사마다 네이티브 메모리가 남았다
                anomalyResult?.AnomalyMap?.Dispose();
                roiImage?.Dispose();
            }

            return result;
        }

        public override VisionToolBase Clone()
        {
            return new AnomalyTool
            {
                Name = this.Name,
                ModelPath = this.ModelPath,
                InputSize = this.InputSize,
                AnomalyThreshold = this.AnomalyThreshold,
                DrawOverlay = this.DrawOverlay,
                ShowHeatmap = this.ShowHeatmap,
                HeatmapOpacity = this.HeatmapOpacity,
                CalibrationFolder = this.CalibrationFolder,
                CalibrationSigma = this.CalibrationSigma,
                UseROI = this.UseROI,
                ROI = this.ROI
            };
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string> { "Success", "AnomalyScore", "IsNormal" };
        }
    }

    /// <summary>
    /// 이상 탐지 결과
    /// </summary>
    public class AnomalyResult
    {
        public float AnomalyScore { get; set; }
        public Mat? AnomalyMap { get; set; }
    }

    /// <summary>
    /// Anomaly Detection ONNX 추론 엔진.
    /// anomalib 호환 모델 (PatchCore, FastFlow, EfficientAD 등)을 지원합니다.
    /// </summary>
    public class AnomalyOnnxEngine : OnnxModelBase
    {
        public AnomalyOnnxEngine(string modelPath)
        {
            LoadModel(modelPath);
        }

        public AnomalyResult Detect(Mat image, int inputSize)
        {
            if (_session == null)
                return new AnomalyResult { AnomalyScore = 0 };

            var tensor = PreprocessImageNet(image, inputSize, inputSize);
            var inputs = CreateInput(GetInputName(), tensor);

            using var outputs = _session.Run(inputs);

            var result = new AnomalyResult();
            bool scoreFound = false;

            // 출력 이름별 파싱 (anomalib 모델 호환)
            // 규약: 점수 [1] 또는 [1,1] (이름에 score) · 이상 맵 [1,1,H,W] (이름에 map). 둘 중 하나는 있어야 한다.
            // 모양을 확인하지 않고 아무 출력의 첫 값을 점수로 쓰면 분류 모델 같은 엉뚱한 모델이 그럴듯한
            // 점수를 내고, 아무 출력도 못 알아보면 점수 0 = '정상' 이 되어 불량이 합격한다.
            foreach (var output in outputs)
            {
                var outTensor = output.AsTensor<float>();
                var dims = outTensor.Dimensions;

                string name = output.Name.ToLower();
                bool scalarShape = (dims.Length == 1 && dims[0] == 1) ||
                                   (dims.Length == 2 && dims[0] == 1 && dims[1] == 1);
                bool mapShape = dims.Length == 4 && dims[0] == 1 && dims[1] == 1;

                if (scalarShape && (name.Contains("score") || !name.Contains("map")))
                {
                    // anomaly score
                    result.AnomalyScore = dims.Length == 2
                        ? outTensor[0, 0]
                        : outTensor[0];
                    scoreFound = true;
                }
                else if (mapShape)
                {
                    // anomaly map: [1, 1, H, W]
                    int mapH = dims[2];
                    int mapW = dims[3];
                    var mapMat = new Mat(mapH, mapW, MatType.CV_32FC1);

                    unsafe
                    {
                        float* ptr = (float*)mapMat.Data;
                        for (int y = 0; y < mapH; y++)
                            for (int x = 0; x < mapW; x++)
                                ptr[y * mapW + x] = Math.Clamp(outTensor[0, 0, y, x], 0f, 1f);
                    }

                    result.AnomalyMap = mapMat;
                }
            }

            if (!scoreFound)
            {
                if (result.AnomalyMap == null)
                {
                    var shapes = string.Join(", ", outputs.Select(o =>
                        $"{o.Name}[{string.Join("x", o.AsTensor<float>().Dimensions.ToArray())}]"));
                    throw new InvalidOperationException(
                        $"이상 탐지 모델 규약이 아닙니다 — 출력 {shapes}. " +
                        "점수 [1] 또는 [1,1], 또는 이상 맵 [1,1,H,W] 출력이 필요합니다.");
                }

                // 맵만 내는 모델 — 맵의 최댓값을 점수로 (anomalib 의 pred_score 정의와 같다)
                Cv2.MinMaxLoc(result.AnomalyMap, out _, out double maxVal);
                result.AnomalyScore = (float)maxVal;
            }

            return result;
        }
    }
}
