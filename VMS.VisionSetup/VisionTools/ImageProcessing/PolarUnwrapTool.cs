using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.ImageProcessing
{
    public enum PolarUnwrapDirection
    {
        /// <summary>시계방향(CW) — 0° 시작점에서 오른쪽으로 진행.</summary>
        Clockwise,
        /// <summary>반시계방향(CCW) — Cognex CogPolarUnwrapTool 기본값.</summary>
        Counterclockwise
    }

    /// <summary>
    /// 원형/원통 표면의 라벨을 극좌표 변환으로 직사각형으로 펼치는 도구.
    /// Cognex CogPolarUnwrapTool 대응. WarpPolar를 후속 OCR/CodeReader/PatternMatch와 조합.
    ///
    /// 입력: 원 중심 (CenterX, CenterY) + 내반경 + 외반경 + 시작 각도 + 방향
    /// 출력: 가로 = 둘레 근사(OutputWidth), 세로 = 외반경−내반경 (OutputHeight 0이면 자동)
    /// Center는 CircleFit 결과를 Coordinates 연결로 자동 주입 가능 (별도 작업).
    /// </summary>
    public class PolarUnwrapTool : VisionToolBase
    {
        private double _centerX;
        public double CenterX
        {
            get => _centerX;
            set => SetProperty(ref _centerX, value);
        }

        private double _centerY;
        public double CenterY
        {
            get => _centerY;
            set => SetProperty(ref _centerY, value);
        }

        private double _innerRadius = 50;
        public double InnerRadius
        {
            get => _innerRadius;
            set => SetProperty(ref _innerRadius, Math.Max(0, value));
        }

        private double _outerRadius = 200;
        public double OuterRadius
        {
            get => _outerRadius;
            set => SetProperty(ref _outerRadius, Math.Max(1, value));
        }

        private double _startAngleDeg;
        /// <summary>펼침 시작 각도(°). 0이면 오른쪽(3시 방향)에서 시작. 90 = 위, 180 = 왼쪽.</summary>
        public double StartAngleDeg
        {
            get => _startAngleDeg;
            set => SetProperty(ref _startAngleDeg, value);
        }

        private PolarUnwrapDirection _direction = PolarUnwrapDirection.Counterclockwise;
        public PolarUnwrapDirection Direction
        {
            get => _direction;
            set => SetProperty(ref _direction, value);
        }

        private int _outputWidth;
        /// <summary>0이면 둘레 근사값 (2π × 외반경) 자동 사용.</summary>
        public int OutputWidth
        {
            get => _outputWidth;
            set => SetProperty(ref _outputWidth, Math.Max(0, value));
        }

        private int _outputHeight;
        /// <summary>0이면 외반경−내반경 자동 사용.</summary>
        public int OutputHeight
        {
            get => _outputHeight;
            set => SetProperty(ref _outputHeight, Math.Max(0, value));
        }

        public PolarUnwrapTool()
        {
            Name = "Polar Unwrap";
            ToolType = "PolarUnwrapTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                if (OuterRadius <= InnerRadius)
                {
                    result.Success = false;
                    result.Message = "OuterRadius는 InnerRadius보다 커야 합니다.";
                    return result;
                }

                int outW = OutputWidth > 0
                    ? OutputWidth
                    : (int)Math.Round(2 * Math.PI * OuterRadius);
                int outH = OutputHeight > 0
                    ? OutputHeight
                    : (int)Math.Round(OuterRadius - InnerRadius);
                if (outW < 4 || outH < 4)
                {
                    result.Success = false;
                    result.Message = $"출력 크기가 너무 작습니다 ({outW}x{outH}). Outer/Inner radius 확인.";
                    return result;
                }

                // OpenCV warpPolar 컨벤션: dst rows = angle 축, dst cols = radius 축.
                // (문서 표현은 모호하지만 실험적으로 확정 — rows가 0..2π를, cols가 0..maxRadius를 매핑)
                // → dsize.width = radius 해상도, dsize.height = angle 해상도로 호출 후 transpose.
                int radiusFull = (int)Math.Round(OuterRadius);
                int angularRows = outW; // 출력 가로(=둘레)에 해당하는 행 수
                using var polarFull = new Mat();
                Cv2.WarpPolar(
                    inputImage, polarFull,
                    new Size(radiusFull, angularRows),
                    new Point2f((float)CenterX, (float)CenterY),
                    (float)OuterRadius,
                    InterpolationFlags.Linear,
                    WarpPolarMode.Linear);

                // polarFull: rows=angle (angularRows), cols=radius (radiusFull, 0..outerR)
                // 우리는 radius ∈ [innerR, outerR]만 원함 — 해당 col 범위 crop
                int innerCol = (int)Math.Round(InnerRadius);
                int radiusWidth = radiusFull - innerCol;
                using var ringRaw = new Mat(polarFull, new Rect(innerCol, 0, radiusWidth, angularRows));

                // Transpose: rows↔cols 교환 → rows=radius, cols=angle (= circumference)
                using var ringT = new Mat();
                Cv2.Transpose(ringRaw, ringT);

                // 사용자 지정 출력 크기로 리사이즈 (필요 시)
                Mat ring;
                if (ringT.Width != outW || ringT.Height != outH)
                {
                    ring = new Mat();
                    Cv2.Resize(ringT, ring, new Size(outW, outH), 0, 0, InterpolationFlags.Linear);
                }
                else ring = ringT.Clone();

                // 방향 + 시작 각도 조정
                Mat oriented = ApplyOrientation(ring, outW, outH);
                ring.Dispose();

                result.OutputImage = oriented;
                // OverlayImage = 펼친 결과 그대로 — Result Image 모드에서 사용자가 직접 확인 가능.
                // 원본 위치 표시는 결과 폴리곤(Graphics)으로 별도 전달.
                result.OverlayImage = oriented.Clone();
                AppendDebugGraphics(result);
                result.Data["CenterX"] = CenterX;
                result.Data["CenterY"] = CenterY;
                result.Data["InnerRadius"] = InnerRadius;
                result.Data["OuterRadius"] = OuterRadius;
                result.Data["OutputWidth"] = outW;
                result.Data["OutputHeight"] = outH;
                result.Success = true;
                result.Message = $"Polar unwrap {outW}x{outH} (R: {InnerRadius:F0}~{OuterRadius:F0})";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Polar Unwrap 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        private Mat ApplyOrientation(Mat ring, int outW, int outH)
        {
            // WarpPolar default: 각도 0(우측 3시)이 좌측 첫 컬럼, CCW 진행
            // StartAngle만큼 좌우 roll + Direction에 따라 좌우 flip
            int shift = (int)Math.Round(StartAngleDeg / 360.0 * outW) % outW;
            if (shift < 0) shift += outW;

            Mat shifted;
            if (shift == 0)
                shifted = ring.Clone();
            else
            {
                shifted = new Mat(ring.Size(), ring.Type());
                using var left = new Mat(ring, new Rect(shift, 0, outW - shift, outH));
                using var right = new Mat(ring, new Rect(0, 0, shift, outH));
                left.CopyTo(shifted[new Rect(0, 0, outW - shift, outH)]);
                right.CopyTo(shifted[new Rect(outW - shift, 0, shift, outH)]);
            }

            if (Direction == PolarUnwrapDirection.Clockwise)
            {
                var flipped = new Mat();
                Cv2.Flip(shifted, flipped, FlipMode.Y);
                shifted.Dispose();
                return flipped;
            }
            return shifted;
        }

        /// <summary>
        /// 원본 이미지 좌표계에 inner/outer 원 + 시작 각도 라인을 GraphicOverlay로 추가.
        /// MainView가 원본 이미지 위에 그래픽을 렌더 — 펼친 결과 표시와 별개로 영역 확인 가능.
        /// </summary>
        private void AppendDebugGraphics(VisionResult result)
        {
            result.Graphics.Add(new GraphicOverlay
            {
                Type = GraphicType.Circle,
                Position = new Point2d(CenterX, CenterY),
                Radius = OuterRadius,
                Color = new Scalar(0, 255, 255),
                Thickness = 2
            });
            if (InnerRadius > 0)
            {
                result.Graphics.Add(new GraphicOverlay
                {
                    Type = GraphicType.Circle,
                    Position = new Point2d(CenterX, CenterY),
                    Radius = InnerRadius,
                    Color = new Scalar(255, 255, 0),
                    Thickness = 2
                });
            }
            result.Graphics.Add(new GraphicOverlay
            {
                Type = GraphicType.Crosshair,
                Position = new Point2d(CenterX, CenterY),
                Color = new Scalar(0, 0, 255),
                Thickness = 2
            });
            // 시작 각도 라인
            double rad = StartAngleDeg * Math.PI / 180.0;
            result.Graphics.Add(new GraphicOverlay
            {
                Type = GraphicType.Line,
                Position = new Point2d(CenterX, CenterY),
                EndPosition = new Point2d(CenterX + Math.Cos(rad) * OuterRadius,
                                          CenterY - Math.Sin(rad) * OuterRadius),
                Color = new Scalar(0, 255, 0),
                Thickness = 2
            });
        }

        public override List<string> GetAvailableResultKeys() => new()
        {
            "Success", "CenterX", "CenterY", "InnerRadius", "OuterRadius",
            "OutputWidth", "OutputHeight"
        };

        public override VisionToolBase Clone()
        {
            var clone = new PolarUnwrapTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                CenterX = this.CenterX,
                CenterY = this.CenterY,
                InnerRadius = this.InnerRadius,
                OuterRadius = this.OuterRadius,
                StartAngleDeg = this.StartAngleDeg,
                Direction = this.Direction,
                OutputWidth = this.OutputWidth,
                OutputHeight = this.OutputHeight
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
