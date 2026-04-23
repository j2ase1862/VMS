using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.Result;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// 앙상블 판정 모드.
    /// </summary>
    public enum EnsembleMode
    {
        /// <summary>모든 소스 모델이 정상이어야 OK (과검 최소화)</summary>
        And,
        /// <summary>하나라도 정상이면 OK (미검 최소화)</summary>
        Or,
        /// <summary>가중치 합산 점수로 판정 (DetectionWeight × det_fail + AnomalyWeight × anomaly_score > WeightedThreshold ⇒ NG)</summary>
        Weighted,
        /// <summary>두 모델 판정이 일치해야 고신뢰. 불일치는 검토 플래그로 처리</summary>
        Consensus
    }

    /// <summary>
    /// EnsembleTool 소스 결과 — 기본 SourceToolResult + VisionResult 전체 참조.
    /// DetectionCount, AnomalyScore 등 판정 세부 데이터에 접근하기 위해 확장.
    /// </summary>
    public class SourceToolResultEx : SourceToolResult
    {
        public string ToolType { get; set; } = string.Empty;
        public VisionResult? FullResult { get; set; }
    }

    /// <summary>
    /// DetectionTool(알려진 결함) + AnomalyTool(비정상 상태 교차 체크) 등
    /// 여러 딥러닝 도구 결과를 결합하여 과검/미검을 동시에 개선하는 앙상블 판정 도구.
    /// </summary>
    public partial class EnsembleTool : VisionToolBase
    {
        [ObservableProperty]
        private EnsembleMode _mode = EnsembleMode.And;

        /// <summary>Weighted 모드: Detection 실패 점수에 곱할 가중치 (0~1)</summary>
        [ObservableProperty]
        private double _detectionWeight = 0.5;

        /// <summary>Weighted 모드: Anomaly 점수에 곱할 가중치 (0~1)</summary>
        [ObservableProperty]
        private double _anomalyWeight = 0.5;

        /// <summary>Weighted 모드: 이 값을 초과하면 NG</summary>
        [ObservableProperty]
        private double _weightedThreshold = 0.5;

        [ObservableProperty]
        private bool _drawOverlay = true;

        /// <summary>VisionService가 Execute 호출 전에 채워주는 소스 결과 목록</summary>
        public List<SourceToolResultEx> SourceResults { get; } = new();

        public EnsembleTool()
        {
            Name = "Ensemble";
            ToolType = "EnsembleTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var sw = Stopwatch.StartNew();
            var result = new VisionResult();

            if (SourceResults.Count == 0)
            {
                result.Success = false;
                result.Message = "소스 도구가 연결되지 않았습니다. Detection / Anomaly 도구를 Result 연결로 붙이세요.";
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
                return result;
            }

            bool ok;
            string detail;

            switch (Mode)
            {
                case EnsembleMode.Or:
                    ok = SourceResults.Any(s => s.Success);
                    detail = $"OR: {SourceResults.Count(s => s.Success)}/{SourceResults.Count} passed";
                    break;

                case EnsembleMode.Weighted:
                    (ok, detail) = JudgeWeighted();
                    break;

                case EnsembleMode.Consensus:
                    int passCount = SourceResults.Count(s => s.Success);
                    bool allAgree = passCount == 0 || passCount == SourceResults.Count;
                    if (!allAgree)
                    {
                        ok = false;
                        detail = "CONSENSUS: 모델 간 판정 불일치 — 검토 필요";
                    }
                    else
                    {
                        ok = passCount == SourceResults.Count;
                        detail = ok ? "CONSENSUS: 전원 정상" : "CONSENSUS: 전원 이상";
                    }
                    break;

                case EnsembleMode.And:
                default:
                    ok = SourceResults.All(s => s.Success);
                    detail = $"AND: {SourceResults.Count(s => s.Success)}/{SourceResults.Count} passed";
                    break;
            }

            result.Success = ok;
            result.Message = ok ? $"OK — {detail}" : $"NG — {detail}";

            // 소스 세부 데이터 포워딩
            int detectionCount = 0;
            float anomalyScore = 0f;
            foreach (var s in SourceResults)
            {
                if (s.FullResult == null) continue;
                if (s.FullResult.Data.TryGetValue("DetectionCount", out var dc) && dc is int n)
                    detectionCount += n;
                if (s.FullResult.Data.TryGetValue("AnomalyScore", out var asv))
                {
                    anomalyScore = asv switch
                    {
                        float f => System.Math.Max(anomalyScore, f),
                        double d => System.Math.Max(anomalyScore, (float)d),
                        _ => anomalyScore
                    };
                }
            }

            result.Data["Success"] = ok;
            result.Data["Mode"] = Mode.ToString();
            result.Data["SourceCount"] = SourceResults.Count;
            result.Data["DetectionCount"] = detectionCount;
            result.Data["AnomalyScore"] = anomalyScore;

            if (DrawOverlay)
            {
                var overlay = GetColorOverlayBase(inputImage);
                var color = ok ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
                string label = ok ? "OK" : "NG";
                Cv2.PutText(overlay, $"{label}: {detail}",
                    new Point(10, 30),
                    HersheyFonts.HersheySimplex, 0.8, color, 2);
                Cv2.Rectangle(overlay,
                    new Rect(0, 0, overlay.Width, overlay.Height),
                    color, 4);
                result.OverlayImage = overlay;

                result.Graphics.Add(new GraphicOverlay
                {
                    Type = GraphicType.Text,
                    Position = new Point2d(10, 30),
                    Text = $"{label}: {detail}",
                    Color = color
                });
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        /// <summary>
        /// Weighted 모드: Detection 실패 비율 × α + Anomaly 최대 점수 × β 가 임계값 초과 시 NG.
        /// Detection 실패 비율 = (Fail / Total). Anomaly 점수 = 모든 Anomaly 소스 중 최댓값 (0~1 정규화 가정).
        /// </summary>
        private (bool ok, string detail) JudgeWeighted()
        {
            int detTotal = 0, detFail = 0;
            float anomalyMax = 0f;

            foreach (var s in SourceResults)
            {
                if (s.ToolType == "DetectionTool")
                {
                    detTotal++;
                    if (!s.Success) detFail++;
                }
                else if (s.ToolType == "AnomalyTool" && s.FullResult != null)
                {
                    if (s.FullResult.Data.TryGetValue("AnomalyScore", out var asv))
                    {
                        float v = asv switch
                        {
                            float f => f,
                            double d => (float)d,
                            _ => 0f
                        };
                        if (v > anomalyMax) anomalyMax = v;
                    }
                }
            }

            double detFailRatio = detTotal > 0 ? (double)detFail / detTotal : 0.0;
            double score = DetectionWeight * detFailRatio + AnomalyWeight * anomalyMax;
            bool ng = score > WeightedThreshold;
            return (!ng, $"WEIGHTED: score={score:F3} (det_fail={detFailRatio:F2}×{DetectionWeight:F2} + anomaly={anomalyMax:F3}×{AnomalyWeight:F2}) vs th={WeightedThreshold:F2}");
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string> { "Success", "SourceCount", "DetectionCount", "AnomalyScore" };
        }

        public override VisionToolBase Clone()
        {
            var clone = new EnsembleTool
            {
                Name = Name,
                ToolType = ToolType,
                IsEnabled = IsEnabled,
                Mode = Mode,
                DetectionWeight = DetectionWeight,
                AnomalyWeight = AnomalyWeight,
                WeightedThreshold = WeightedThreshold,
                DrawOverlay = DrawOverlay
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
