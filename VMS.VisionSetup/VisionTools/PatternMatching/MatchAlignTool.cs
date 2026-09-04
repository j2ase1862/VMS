using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.VisionTools.PatternMatching
{
    /// <summary>얼라인 계산 방식.</summary>
    public enum MatchAlignMode
    {
        /// <summary>1점 — 매칭 1개의 중심·각도를 기준 포즈와 비교 (Δθ = 매칭 각도 차).</summary>
        SinglePoint,
        /// <summary>2점 — 매칭 2개의 중심으로 회전·이동을 유도 (Δθ = 두 점을 잇는 벡터의 회전).
        /// 부품 양단의 특징 2개가 한 FOV 에 함께 보일 때 사용 — 각도 정밀도가 1점보다 높다.</summary>
        TwoPoint
    }

    /// <summary>
    /// MatchAlignTool — 표준 2D 매치 기반 얼라인 (Standard 2D Match-based Align).
    ///
    /// Feature Match 가 산출한 현재 포즈(CenterX/CenterY/Angle)를 기준(Origin/Master)
    /// 포즈와 비교해 변위량 ΔX/ΔY/Δθ 를 계산한다. 캘리브레이션(또는 스텝 Resolution)이
    /// 있으면 mm 변위도 함께 산출하고, 선택적으로 핸드아이 행렬(상위 2x2)로 로봇/스테이지
    /// 좌표계 변위로 변환한다. Cognex PMAlign + Fixture 보정 흐름의 대체.
    ///
    /// 기준 포즈:
    /// - 학습 기준(기본): Feature Match 학습 시점의 패턴 중심(TrainedCenterX/Y)을 기준으로,
    ///   기준 각도는 학습 ROI 각도(TrainedAngle, 축 정렬이면 0°) — 별도 등록 없이 학습만 하면 동작한다.
    /// - 수동 기준: 설정 패널의 [현재 매칭을 기준으로 등록]으로 현재 포즈를 캡처하거나
    ///   RefX/RefY/RefTheta 를 직접 입력한다.
    /// </summary>
    public class MatchAlignTool : VisionToolBase
    {
        private MatchAlignMode _mode = MatchAlignMode.SinglePoint;
        /// <summary>얼라인 계산 방식 (1점 / 2점).</summary>
        public MatchAlignMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        // ── 기준(Origin/Master) 포즈 ──

        private bool _useTrainedReference = true;
        /// <summary>true 면 소스 매칭의 학습 중심(TrainedCenterX/Y, θ=TrainedAngle)을 기준으로 사용.</summary>
        public bool UseTrainedReference
        {
            get => _useTrainedReference;
            set => SetProperty(ref _useTrainedReference, value);
        }

        private double _refX;
        /// <summary>수동 기준 X (px).</summary>
        public double RefX
        {
            get => _refX;
            set => SetProperty(ref _refX, value);
        }

        private double _refY;
        /// <summary>수동 기준 Y (px).</summary>
        public double RefY
        {
            get => _refY;
            set => SetProperty(ref _refY, value);
        }

        private double _refTheta;
        /// <summary>수동 기준 각도 (°). 1점 모드 전용 — 2점 모드는 두 점의 벡터가 각도를 정의.</summary>
        public double RefTheta
        {
            get => _refTheta;
            set => SetProperty(ref _refTheta, value);
        }

        private double _refX2;
        /// <summary>수동 기준 X — 2번 포인트 (px, 2점 모드 전용).</summary>
        public double RefX2
        {
            get => _refX2;
            set => SetProperty(ref _refX2, value);
        }

        private double _refY2;
        /// <summary>수동 기준 Y — 2번 포인트 (px, 2점 모드 전용).</summary>
        public double RefY2
        {
            get => _refY2;
            set => SetProperty(ref _refY2, value);
        }

        // ── 로봇/스테이지 좌표 변환 (핸드아이 행렬 상위 2x2) ──
        // 변위(Δ)는 벡터라 병진 성분이 소거되므로 선형부(회전·스케일·반전)만 필요하다.

        private bool _enableRobotTransform;
        /// <summary>켜면 Δ(mm 우선, 없으면 px)에 2x2 행렬을 적용해 RobotDX/DY/DTheta 를 출력.</summary>
        public bool EnableRobotTransform
        {
            get => _enableRobotTransform;
            set => SetProperty(ref _enableRobotTransform, value);
        }

        private double _robotM11 = 1;
        public double RobotM11 { get => _robotM11; set => SetProperty(ref _robotM11, value); }

        private double _robotM12;
        public double RobotM12 { get => _robotM12; set => SetProperty(ref _robotM12, value); }

        private double _robotM21;
        public double RobotM21 { get => _robotM21; set => SetProperty(ref _robotM21, value); }

        private double _robotM22 = 1;
        public double RobotM22 { get => _robotM22; set => SetProperty(ref _robotM22, value); }

        private double _robotThetaSign = 1;
        /// <summary>로봇 좌표계의 회전 방향 부호 (+1/−1).</summary>
        public double RobotThetaSign
        {
            get => _robotThetaSign;
            set => SetProperty(ref _robotThetaSign, value);
        }

        // ── 판정 — 변위가 허용 범위 이내인지 (GeometryTool 컨벤션) ──

        private bool _enableJudgment;
        /// <summary>판정 사용 — 꺼져 있으면 변위 계산만 한다.</summary>
        public bool EnableJudgment
        {
            get => _enableJudgment;
            set => SetProperty(ref _enableJudgment, value);
        }

        private GeometryJudgmentUnit _judgmentUnit = GeometryJudgmentUnit.Mm;
        /// <summary>위치 변위 판정 단위. Mm 는 캘리브레이션/스텝 Resolution 필요 — 변환 불가 시 판정 실패.</summary>
        public GeometryJudgmentUnit JudgmentUnit
        {
            get => _judgmentUnit;
            set => SetProperty(ref _judgmentUnit, value);
        }

        private double _maxDeltaXY = 1.0;
        /// <summary>위치 변위 허용 반경 (√(ΔX²+ΔY²) ≤ 이 값이면 합격, 단위는 JudgmentUnit).</summary>
        public double MaxDeltaXY
        {
            get => _maxDeltaXY;
            set => SetProperty(ref _maxDeltaXY, Math.Max(0, value));
        }

        private double _maxDeltaTheta = 1.0;
        /// <summary>각도 변위 허용 (|Δθ| ≤ 이 값이면 합격, °).</summary>
        public double MaxDeltaTheta
        {
            get => _maxDeltaTheta;
            set => SetProperty(ref _maxDeltaTheta, Math.Max(0, value));
        }

        private bool _drawOverlay = true;
        /// <summary>기준→현재 변위 화살표·수치 오버레이 표시.</summary>
        public bool DrawOverlay
        {
            get => _drawOverlay;
            set => SetProperty(ref _drawOverlay, value);
        }

        /// <summary>
        /// VisionService 가 Execute 전에 채워주는 소스 매칭 결과 (Result 연결의 첫 소스).
        /// CenterX/CenterY/Angle 을 가진 결과(FeatureMatch 등)여야 한다.
        /// </summary>
        public VisionResult? SourceMatchResult { get; set; }

        /// <summary>소스 도구 이름 (메시지·설정 패널 표시용).</summary>
        public string SourceToolName { get; set; } = string.Empty;

        /// <summary>2점 모드의 두 번째 소스 매칭 결과 (Result 연결의 두 번째 소스 — 연결 순서).</summary>
        public VisionResult? SourceMatchResult2 { get; set; }

        /// <summary>두 번째 소스 도구 이름.</summary>
        public string SourceToolName2 { get; set; } = string.Empty;

        public MatchAlignTool()
        {
            Name = "Match Align";
            ToolType = "MatchAlignTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var sw = Stopwatch.StartNew();
            var result = new VisionResult();

            try
            {
                // ── 모드별 포즈 취득 + Δ 계산 (px / °) ──
                double dxPx, dyPx, dTheta;
                double refX, refY, curX, curY;                 // 대표점 (1점: 그 점, 2점: 중심)
                double refX2 = 0, refY2 = 0, curX2 = 0, curY2 = 0;   // 2점 모드 보조점
                double? dxMm = null, dyMm = null;
                var cal = VisionService.Instance.EffectiveCalibration;

                var src = SourceMatchResult;
                if (src == null)
                {
                    result.Success = false;
                    result.Message = "매칭 소스가 없습니다 — Feature Match 를 Result 로 연결하세요.";
                    return result;
                }
                if (!src.Success
                    || !TryGet(src, "CenterX", out var p1x)
                    || !TryGet(src, "CenterY", out var p1y))
                {
                    result.Success = false;
                    result.Message = $"소스 매칭 실패({SourceToolName}) — 변위를 계산할 수 없습니다.";
                    return result;
                }

                if (Mode == MatchAlignMode.TwoPoint)
                {
                    var src2 = SourceMatchResult2;
                    if (src2 == null)
                    {
                        result.Success = false;
                        result.Message = "2점 모드는 매칭 소스 2개가 필요합니다 — Feature Match 2개를 Result 로 연결하세요.";
                        return result;
                    }
                    if (!src2.Success
                        || !TryGet(src2, "CenterX", out var p2x)
                        || !TryGet(src2, "CenterY", out var p2y))
                    {
                        result.Success = false;
                        result.Message = $"2번 포인트 매칭 실패({SourceToolName2}) — 변위를 계산할 수 없습니다.";
                        return result;
                    }

                    // 기준 2점
                    double o1x, o1y, o2x, o2y;
                    if (UseTrainedReference)
                    {
                        if (!TryGet(src, "TrainedCenterX", out o1x) || !TryGet(src, "TrainedCenterY", out o1y) ||
                            !TryGet(src2, "TrainedCenterX", out o2x) || !TryGet(src2, "TrainedCenterY", out o2y))
                        {
                            result.Success = false;
                            result.Message = "학습 기준을 찾을 수 없습니다 — 두 소스 모두 패턴을 학습하거나 수동 기준을 사용하세요.";
                            return result;
                        }
                    }
                    else
                    {
                        o1x = RefX; o1y = RefY; o2x = RefX2; o2y = RefY2;
                    }

                    double baseLen = Math.Sqrt((o2x - o1x) * (o2x - o1x) + (o2y - o1y) * (o2y - o1y));
                    if (baseLen < 1e-6)
                    {
                        result.Success = false;
                        result.Message = "기준 두 점이 동일합니다 — 서로 다른 두 특징을 기준으로 잡으세요.";
                        return result;
                    }

                    // Δθ = 두 점을 잇는 벡터의 회전 (매칭 각도보다 장기 기저선이라 정밀)
                    double angO = Math.Atan2(o2y - o1y, o2x - o1x) * 180.0 / Math.PI;
                    double angP = Math.Atan2(p2y - p1y, p2x - p1x) * 180.0 / Math.PI;
                    dTheta = FeatureMatchTool.NormalizeAngle(angP - angO);

                    // ΔX/ΔY = 중심점(centroid) 이동
                    double ocx = (o1x + o2x) / 2.0, ocy = (o1y + o2y) / 2.0;
                    double pcx = (p1x + p2x) / 2.0, pcy = (p1y + p2y) / 2.0;
                    dxPx = pcx - ocx;
                    dyPx = pcy - ocy;

                    double curLen = Math.Sqrt((p2x - p1x) * (p2x - p1x) + (p2y - p1y) * (p2y - p1y));
                    result.Data["ScaleRatio"] = curLen / baseLen;   // 1.0 근처가 정상 — 이탈 시 오검/배율 변화 의심
                    result.Data["Point1X"] = p1x; result.Data["Point1Y"] = p1y;
                    result.Data["Point2X"] = p2x; result.Data["Point2Y"] = p2y;
                    result.Data["Ref1X"] = o1x; result.Data["Ref1Y"] = o1y;
                    result.Data["Ref2X"] = o2x; result.Data["Ref2Y"] = o2y;

                    refX = ocx; refY = ocy; curX = pcx; curY = pcy;
                    refX2 = o2x; refY2 = o2y; curX2 = p2x; curY2 = p2y;
                    result.Data["RefTheta"] = angO;
                    result.Data["CurrentTheta"] = angP;

                    if (cal != null)
                    {
                        // 각 점을 mm 로 변환 후 중심 차분 (호모그래피에서도 정확)
                        var (p1mx, p1my) = cal.PixelToMm(p1x, p1y);
                        var (p2mx, p2my) = cal.PixelToMm(p2x, p2y);
                        var (o1mx, o1my) = cal.PixelToMm(o1x, o1y);
                        var (o2mx, o2my) = cal.PixelToMm(o2x, o2y);
                        dxMm = (p1mx + p2mx) / 2.0 - (o1mx + o2mx) / 2.0;
                        dyMm = (p1my + p2my) / 2.0 - (o1my + o2my) / 2.0;
                    }
                }
                else
                {
                    double curTheta = TryGet(src, "Angle", out var a) ? a : 0;

                    // 기준 포즈 결정
                    double refTheta;
                    if (UseTrainedReference)
                    {
                        if (!TryGet(src, "TrainedCenterX", out refX) ||
                            !TryGet(src, "TrainedCenterY", out refY))
                        {
                            result.Success = false;
                            result.Message = "학습 기준을 찾을 수 없습니다 — 소스에서 패턴을 학습하거나 수동 기준을 사용하세요.";
                            return result;
                        }
                        // 기준 각도 = 학습 ROI 각도 (회전 ROI 학습 시 매칭 각도가 그 값으로 나옴).
                        // 구 레시피/키 없음 → 0° (2026-09-04 실증: 회전 ROI 학습 후 Δθ = ROI 각도 결함 수정)
                        refTheta = TryGet(src, "TrainedAngle", out var trainedAngle) ? trainedAngle : 0;
                    }
                    else
                    {
                        refX = RefX; refY = RefY; refTheta = RefTheta;
                    }

                    curX = p1x; curY = p1y;
                    dxPx = curX - refX;
                    dyPx = curY - refY;
                    dTheta = FeatureMatchTool.NormalizeAngle(curTheta - refTheta);
                    result.Data["RefTheta"] = refTheta;
                    result.Data["CurrentTheta"] = curTheta;

                    if (cal != null)
                    {
                        // 두 점을 각각 변환 후 차분 (호모그래피 캘리브레이션에서도 정확)
                        var (cxMm, cyMm) = cal.PixelToMm(curX, curY);
                        var (rxMm, ryMm) = cal.PixelToMm(refX, refY);
                        dxMm = cxMm - rxMm;
                        dyMm = cyMm - ryMm;
                    }
                }

                result.Data["RefX"] = refX;
                result.Data["RefY"] = refY;
                result.Data["CurrentX"] = curX;
                result.Data["CurrentY"] = curY;
                result.Data["DeltaX"] = dxPx;
                result.Data["DeltaY"] = dyPx;
                result.Data["DeltaTheta"] = dTheta;
                if (dxMm.HasValue)
                {
                    result.Data["DeltaXMm"] = dxMm.Value;
                    result.Data["DeltaYMm"] = dyMm!.Value;
                }

                // ── 로봇/스테이지 좌표 변환 ──
                if (EnableRobotTransform)
                {
                    double bx = dxMm ?? dxPx;
                    double by = dyMm ?? dyPx;
                    result.Data["RobotDX"] = RobotM11 * bx + RobotM12 * by;
                    result.Data["RobotDY"] = RobotM21 * bx + RobotM22 * by;
                    result.Data["RobotDTheta"] = RobotThetaSign * dTheta;
                }

                // ── 메시지 + 판정 ──
                string mmText = dxMm.HasValue ? $" ({dxMm:F3}, {dyMm:F3})mm" : "";
                result.Message = $"Δ=({dxPx:F1}, {dyPx:F1})px{mmText}, Δθ={dTheta:F2}°";
                result.Success = true;

                if (EnableJudgment)
                {
                    double? jx, jy;
                    if (JudgmentUnit == GeometryJudgmentUnit.Mm)
                    {
                        jx = dxMm; jy = dyMm;
                        if (jx == null)
                        {
                            result.Success = false;
                            result.Message += " · 판정 NG: mm 변환 불가 — 캘리브레이션 또는 스텝 Resolution(mm/px)을 설정하세요";
                        }
                    }
                    else
                    {
                        jx = dxPx; jy = dyPx;
                    }

                    if (jx != null && jy != null)
                    {
                        double radial = Math.Sqrt(jx.Value * jx.Value + jy.Value * jy.Value);
                        bool posOk = radial <= MaxDeltaXY;
                        bool angOk = Math.Abs(dTheta) <= MaxDeltaTheta;
                        result.Data["JudgmentRadial"] = radial;
                        result.Data["JudgmentPass"] = posOk && angOk;
                        if (!posOk || !angOk)
                        {
                            result.Success = false;
                            string unit = JudgmentUnit == GeometryJudgmentUnit.Mm ? "mm" : "px";
                            result.Message += $" · 판정 NG: {(posOk ? "" : $"위치 {radial:F3}{unit} > {MaxDeltaXY}{unit} ")}" +
                                              $"{(angOk ? "" : $"각도 |{dTheta:F2}°| > {MaxDeltaTheta}°")}".TrimEnd();
                        }
                        else
                        {
                            result.Message += " · 판정 OK";
                        }
                    }
                }

                // ── 오버레이: 기준(노랑 십자) → 현재(초록/빨강 원) 화살표 + 수치 ──
                if (DrawOverlay)
                {
                    var overlay = GetColorOverlayBase(inputImage);
                    var refPt = new Point((int)refX, (int)refY);
                    var curPt = new Point((int)curX, (int)curY);
                    var color = result.Success ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);

                    if (Mode == MatchAlignMode.TwoPoint)
                    {
                        // 두 포인트 쌍 + 기저선 (대표점 = 중심)
                        var o2Pt = new Point((int)refX2, (int)refY2);
                        var p2Pt = new Point((int)curX2, (int)curY2);
                        var o1Pt = new Point((int)(refX * 2 - refX2), (int)(refY * 2 - refY2));
                        var p1Pt = new Point((int)(curX * 2 - curX2), (int)(curY * 2 - curY2));
                        Cv2.Line(overlay, p1Pt, p2Pt, color, 1, LineTypes.AntiAlias);
                        Cv2.DrawMarker(overlay, o1Pt, new Scalar(0, 255, 255), MarkerTypes.Cross, 20, 2);
                        Cv2.DrawMarker(overlay, o2Pt, new Scalar(0, 255, 255), MarkerTypes.Cross, 20, 2);
                        Cv2.Circle(overlay, p1Pt, 5, color, 2);
                        Cv2.Circle(overlay, p2Pt, 5, color, 2);
                    }

                    Cv2.DrawMarker(overlay, refPt, new Scalar(0, 255, 255),
                        MarkerTypes.Cross, 24, 2);
                    Cv2.ArrowedLine(overlay, refPt, curPt, color, 2, tipLength: 0.15);
                    Cv2.Circle(overlay, curPt, 6, color, 2);

                    var textPt = new Point(curPt.X + 10, curPt.Y - 10);
                    Cv2.PutText(overlay, $"dX={dxPx:F1} dY={dyPx:F1} dTh={dTheta:F2}",
                        textPt, HersheyFonts.HersheySimplex, 0.55, new Scalar(255, 255, 255), 1);
                    result.OverlayImage = overlay;
                }

                // 위치 연산 도구 — 이미지는 변형하지 않으므로 passthrough (FeatureMatch 와 동일 근거)
                result.OutputImage = inputImage.Clone();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"실행 오류: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }

            return result;
        }

        private static bool TryGet(VisionResult src, string key, out double value)
        {
            if (src.Data.TryGetValue(key, out var obj))
            {
                value = Convert.ToDouble(obj);
                return true;
            }
            value = 0;
            return false;
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success",
                "RefX", "RefY", "RefTheta",
                "CurrentX", "CurrentY", "CurrentTheta",
                "DeltaX", "DeltaY", "DeltaTheta",
                "DeltaXMm", "DeltaYMm",
                "RobotDX", "RobotDY", "RobotDTheta",
                "JudgmentRadial", "JudgmentPass",
                // 2점 모드
                "Point1X", "Point1Y", "Point2X", "Point2Y",
                "Ref1X", "Ref1Y", "Ref2X", "Ref2Y", "ScaleRatio"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new MatchAlignTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                Mode = this.Mode,
                UseTrainedReference = this.UseTrainedReference,
                RefX = this.RefX,
                RefY = this.RefY,
                RefTheta = this.RefTheta,
                RefX2 = this.RefX2,
                RefY2 = this.RefY2,
                EnableRobotTransform = this.EnableRobotTransform,
                RobotM11 = this.RobotM11,
                RobotM12 = this.RobotM12,
                RobotM21 = this.RobotM21,
                RobotM22 = this.RobotM22,
                RobotThetaSign = this.RobotThetaSign,
                EnableJudgment = this.EnableJudgment,
                JudgmentUnit = this.JudgmentUnit,
                MaxDeltaXY = this.MaxDeltaXY,
                MaxDeltaTheta = this.MaxDeltaTheta,
                DrawOverlay = this.DrawOverlay
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
