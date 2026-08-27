using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.VisionTools.PatternMatching
{
    /// <summary>
    /// MultiStepAlignTool — 2-스텝(2-FOV) 매치 기반 얼라인.
    ///
    /// 카메라 1대가 이동(로봇/스테이지)하며 두 스텝에서 부품 양단의 특징을 하나씩
    /// 매칭할 때, 두 스텝의 포즈(StepPoseStore 경유)를 하나의 좌표계로 합쳐
    /// 기준 대비 ΔX/ΔY/Δθ 를 계산한다. 부품이 한 FOV 에 다 들어오지 않는
    /// 대형 부품(패널·프레임) 얼라인의 표준 구성.
    ///
    /// 좌표 합성: 점A = A스텝 로컬 좌표, 점B = B스텝 로컬 좌표 + Baseline(두 촬영
    /// 위치 간 오프셋). 두 스텝 모두 캘리브레이션(또는 스텝 Resolution)이 있으면
    /// mm 로, 없으면 px 로 합성한다 — Baseline 단위도 이에 맞출 것 (mm 권장).
    /// 카메라 이동이 반복 정밀하다는 전제가 필요하다.
    ///
    /// 이 도구는 마지막 스텝(B 스텝)의 워크스페이스에 배치한다. 기준 등록은
    /// 기준 부품으로 두 스텝을 Run 한 뒤 설정 패널의 [현재 두 점을 기준으로 등록].
    /// </summary>
    public class MultiStepAlignTool : VisionToolBase
    {
        // ── 소스 지정 (스텝 Id + 툴 Id — 레시피 저장 시 안정 키) ──

        private string _sourceStepIdA = string.Empty;
        public string SourceStepIdA
        {
            get => _sourceStepIdA;
            set => SetProperty(ref _sourceStepIdA, value);
        }

        private string _sourceToolIdA = string.Empty;
        public string SourceToolIdA
        {
            get => _sourceToolIdA;
            set => SetProperty(ref _sourceToolIdA, value);
        }

        private string _sourceStepIdB = string.Empty;
        public string SourceStepIdB
        {
            get => _sourceStepIdB;
            set => SetProperty(ref _sourceStepIdB, value);
        }

        private string _sourceToolIdB = string.Empty;
        public string SourceToolIdB
        {
            get => _sourceToolIdB;
            set => SetProperty(ref _sourceToolIdB, value);
        }

        // ── FOV 간 기준 벡터 (B 촬영 위치 − A 촬영 위치, 합성 좌표계 단위) ──

        private double _baselineX;
        /// <summary>B 스텝 FOV 원점의 A 스텝 대비 X 오프셋 (mm 권장 — 캘리브 없으면 px).</summary>
        public double BaselineX
        {
            get => _baselineX;
            set => SetProperty(ref _baselineX, value);
        }

        private double _baselineY;
        /// <summary>B 스텝 FOV 원점의 A 스텝 대비 Y 오프셋.</summary>
        public double BaselineY
        {
            get => _baselineY;
            set => SetProperty(ref _baselineY, value);
        }

        // ── 기준(Origin) 2점 — 합성 좌표계 (캡처 또는 수동 입력) ──

        private double _refAX;
        public double RefAX { get => _refAX; set => SetProperty(ref _refAX, value); }

        private double _refAY;
        public double RefAY { get => _refAY; set => SetProperty(ref _refAY, value); }

        private double _refBX;
        public double RefBX { get => _refBX; set => SetProperty(ref _refBX, value); }

        private double _refBY;
        public double RefBY { get => _refBY; set => SetProperty(ref _refBY, value); }

        private bool _hasReference;
        /// <summary>기준이 등록되었는지 — 미등록 상태로 Run 하면 명확히 실패.</summary>
        public bool HasReference
        {
            get => _hasReference;
            set => SetProperty(ref _hasReference, value);
        }

        // ── 사이클 규칙 ──

        private bool _requireSameCycle = true;
        /// <summary>AUTO RUN 에서 이번 사이클에 기록된 포즈만 인정 (이전 사이클 잔존값 차단).</summary>
        public bool RequireSameCycle
        {
            get => _requireSameCycle;
            set => SetProperty(ref _requireSameCycle, value);
        }

        // ── 로봇/스테이지 변환 + 판정 (MatchAlign 과 동일 컨벤션) ──

        private bool _enableRobotTransform;
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
        public double RobotThetaSign
        {
            get => _robotThetaSign;
            set => SetProperty(ref _robotThetaSign, value);
        }

        private bool _enableJudgment;
        public bool EnableJudgment
        {
            get => _enableJudgment;
            set => SetProperty(ref _enableJudgment, value);
        }

        private double _maxDeltaXY = 1.0;
        /// <summary>위치 변위 허용 반경 (합성 좌표계 단위 — mm 캘리브 시 mm).</summary>
        public double MaxDeltaXY
        {
            get => _maxDeltaXY;
            set => SetProperty(ref _maxDeltaXY, Math.Max(0, value));
        }

        private double _maxDeltaTheta = 1.0;
        /// <summary>각도 변위 허용 (°).</summary>
        public double MaxDeltaTheta
        {
            get => _maxDeltaTheta;
            set => SetProperty(ref _maxDeltaTheta, Math.Max(0, value));
        }

        private bool _drawOverlay = true;
        public bool DrawOverlay
        {
            get => _drawOverlay;
            set => SetProperty(ref _drawOverlay, value);
        }

        public MultiStepAlignTool()
        {
            Name = "Multi-Step Align";
            ToolType = "MultiStepAlignTool";
        }

        /// <summary>
        /// 저장소에서 두 소스의 현재 포즈를 읽어 합성 좌표계 점 2개로 변환.
        /// 실행과 기준 캡처가 같은 합성 규칙을 쓰도록 단일 정의.
        /// </summary>
        public bool TryComputeCurrentPoints(
            out double ax, out double ay, out double bx, out double by,
            out string unit, out string error)
        {
            ax = ay = bx = by = 0;
            unit = "px";
            error = string.Empty;

            if (string.IsNullOrEmpty(SourceStepIdA) || string.IsNullOrEmpty(SourceToolIdA) ||
                string.IsNullOrEmpty(SourceStepIdB) || string.IsNullOrEmpty(SourceToolIdB))
            {
                error = "소스 스텝/툴이 지정되지 않았습니다 — 설정에서 A/B 소스를 선택하세요.";
                return false;
            }

            var a = StepPoseStore.TryGet(SourceStepIdA, SourceToolIdA, RequireSameCycle);
            if (a == null)
            {
                error = "A 스텝 포즈가 없습니다 — A 스텝이 이번 사이클에서 실행/매칭되었는지 확인하세요.";
                return false;
            }
            var b = StepPoseStore.TryGet(SourceStepIdB, SourceToolIdB, RequireSameCycle);
            if (b == null)
            {
                error = "B 스텝 포즈가 없습니다 — B 스텝이 이번 사이클에서 실행/매칭되었는지 확인하세요.";
                return false;
            }

            // 두 스텝 모두 mm 가 있으면 mm 합성, 아니면 px 합성 (Baseline 단위 일치 필요)
            if (a.XMm.HasValue && b.XMm.HasValue)
            {
                unit = "mm";
                ax = a.XMm.Value; ay = a.YMm!.Value;
                bx = b.XMm.Value + BaselineX; by = b.YMm!.Value + BaselineY;
            }
            else
            {
                unit = "px";
                ax = a.CenterX; ay = a.CenterY;
                bx = b.CenterX + BaselineX; by = b.CenterY + BaselineY;
            }
            return true;
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var sw = Stopwatch.StartNew();
            var result = new VisionResult();

            try
            {
                if (!TryComputeCurrentPoints(out var ax, out var ay, out var bx, out var by,
                        out var unit, out var error))
                {
                    result.Success = false;
                    result.Message = error;
                    return result;
                }

                if (!HasReference)
                {
                    result.Success = false;
                    result.Message = "기준이 등록되지 않았습니다 — 기준 부품으로 두 스텝을 Run 한 뒤 [현재 두 점을 기준으로 등록]하세요.";
                    return result;
                }

                double baseLen = Math.Sqrt(
                    (RefBX - RefAX) * (RefBX - RefAX) + (RefBY - RefAY) * (RefBY - RefAY));
                if (baseLen < 1e-9)
                {
                    result.Success = false;
                    result.Message = "기준 두 점이 동일합니다 — 기준 등록을 다시 하세요.";
                    return result;
                }

                // 2점 강체 정합 — Δθ = 두 점 벡터의 회전, ΔX/ΔY = 중심 이동
                double angO = Math.Atan2(RefBY - RefAY, RefBX - RefAX) * 180.0 / Math.PI;
                double angP = Math.Atan2(by - ay, bx - ax) * 180.0 / Math.PI;
                double dTheta = FeatureMatchTool.NormalizeAngle(angP - angO);

                double ocx = (RefAX + RefBX) / 2.0, ocy = (RefAY + RefBY) / 2.0;
                double pcx = (ax + bx) / 2.0, pcy = (ay + by) / 2.0;
                double dx = pcx - ocx;
                double dy = pcy - ocy;

                double curLen = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

                result.Data["Unit"] = unit;
                result.Data["PointAX"] = ax; result.Data["PointAY"] = ay;
                result.Data["PointBX"] = bx; result.Data["PointBY"] = by;
                result.Data["RefAX"] = RefAX; result.Data["RefAY"] = RefAY;
                result.Data["RefBX"] = RefBX; result.Data["RefBY"] = RefBY;
                result.Data["DeltaX"] = dx;
                result.Data["DeltaY"] = dy;
                result.Data["DeltaTheta"] = dTheta;
                result.Data["ScaleRatio"] = curLen / baseLen;

                if (EnableRobotTransform)
                {
                    result.Data["RobotDX"] = RobotM11 * dx + RobotM12 * dy;
                    result.Data["RobotDY"] = RobotM21 * dx + RobotM22 * dy;
                    result.Data["RobotDTheta"] = RobotThetaSign * dTheta;
                }

                result.Success = true;
                result.Message = $"Δ=({dx:F3}, {dy:F3}){unit}, Δθ={dTheta:F3}°";

                if (EnableJudgment)
                {
                    double radial = Math.Sqrt(dx * dx + dy * dy);
                    bool posOk = radial <= MaxDeltaXY;
                    bool angOk = Math.Abs(dTheta) <= MaxDeltaTheta;
                    result.Data["JudgmentRadial"] = radial;
                    result.Data["JudgmentPass"] = posOk && angOk;
                    if (!posOk || !angOk)
                    {
                        result.Success = false;
                        result.Message += $" · 판정 NG: {(posOk ? "" : $"위치 {radial:F3}{unit} > {MaxDeltaXY}{unit} ")}" +
                                          $"{(angOk ? "" : $"각도 |{dTheta:F3}°| > {MaxDeltaTheta}°")}".TrimEnd();
                    }
                    else
                    {
                        result.Message += " · 판정 OK";
                    }
                }

                // 오버레이 — 두 점이 서로 다른 FOV 라 이미지 좌표로 못 그린다.
                // 요약 패널 텍스트만 표시.
                if (DrawOverlay)
                {
                    var overlay = GetColorOverlayBase(inputImage);
                    var color = result.Success ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
                    Cv2.Rectangle(overlay, new Rect(8, 8, 430, 66), new Scalar(30, 30, 30), -1);
                    Cv2.Rectangle(overlay, new Rect(8, 8, 430, 66), color, 2);
                    Cv2.PutText(overlay, $"MultiStepAlign dX={dx:F2} dY={dy:F2} {unit}",
                        new Point(18, 34), HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 1);
                    Cv2.PutText(overlay, $"dTheta={dTheta:F3} deg  scale={curLen / baseLen:F4}",
                        new Point(18, 60), HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 1);
                    result.OverlayImage = overlay;
                }

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

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "Unit",
                "PointAX", "PointAY", "PointBX", "PointBY",
                "RefAX", "RefAY", "RefBX", "RefBY",
                "DeltaX", "DeltaY", "DeltaTheta", "ScaleRatio",
                "RobotDX", "RobotDY", "RobotDTheta",
                "JudgmentRadial", "JudgmentPass"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new MultiStepAlignTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                SourceStepIdA = this.SourceStepIdA,
                SourceToolIdA = this.SourceToolIdA,
                SourceStepIdB = this.SourceStepIdB,
                SourceToolIdB = this.SourceToolIdB,
                BaselineX = this.BaselineX,
                BaselineY = this.BaselineY,
                RefAX = this.RefAX,
                RefAY = this.RefAY,
                RefBX = this.RefBX,
                RefBY = this.RefBY,
                HasReference = this.HasReference,
                RequireSameCycle = this.RequireSameCycle,
                EnableRobotTransform = this.EnableRobotTransform,
                RobotM11 = this.RobotM11,
                RobotM12 = this.RobotM12,
                RobotM21 = this.RobotM21,
                RobotM22 = this.RobotM22,
                RobotThetaSign = this.RobotThetaSign,
                EnableJudgment = this.EnableJudgment,
                MaxDeltaXY = this.MaxDeltaXY,
                MaxDeltaTheta = this.MaxDeltaTheta,
                DrawOverlay = this.DrawOverlay
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
