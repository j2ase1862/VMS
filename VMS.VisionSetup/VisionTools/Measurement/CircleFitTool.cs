using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VMS.VisionSetup.VisionTools.Measurement
{
    /// <summary>
    /// 원 검출 및 피팅 도구 (Cognex VisionPro CogFindCircleTool 대체)
    /// 여러 Caliper를 사용하여 원을 검출하고 피팅
    /// </summary>
    public class CircleFitTool : VisionToolBase
    {
        // 검색 영역 설정
        private Point2d _centerPoint = new Point2d(200, 200);
        public Point2d CenterPoint
        {
            get => _centerPoint;
            set => SetProperty(ref _centerPoint, value);
        }

        private double _expectedRadius = 100;
        public double ExpectedRadius
        {
            get => _expectedRadius;
            set => SetProperty(ref _expectedRadius, Math.Max(10, value));
        }

        private int _numCalipers = 16;
        public int NumCalipers
        {
            get => _numCalipers;
            set => SetProperty(ref _numCalipers, Math.Max(3, value));
        }

        private double _searchLength = 50;
        public double SearchLength
        {
            get => _searchLength;
            set => SetProperty(ref _searchLength, Math.Max(10, value));
        }

        private double _searchWidth = 5;
        public double SearchWidth
        {
            get => _searchWidth;
            set => SetProperty(ref _searchWidth, Math.Max(1, value));
        }

        // 검색 각도 범위
        private double _startAngle = 0;
        public double StartAngle
        {
            get => _startAngle;
            set => SetProperty(ref _startAngle, value);
        }

        private double _endAngle = 360;
        public double EndAngle
        {
            get => _endAngle;
            set => SetProperty(ref _endAngle, value);
        }

        // Edge 검출 설정
        private EdgePolarity _polarity = EdgePolarity.DarkToLight;
        public EdgePolarity Polarity
        {
            get => _polarity;
            set => SetProperty(ref _polarity, value);
        }

        private double _edgeThreshold = 30;
        public double EdgeThreshold
        {
            get => _edgeThreshold;
            set => SetProperty(ref _edgeThreshold, Math.Max(1, value));
        }

        // 피팅 설정
        private CircleFitMethod _fitMethod = CircleFitMethod.LeastSquares;
        public CircleFitMethod FitMethod
        {
            get => _fitMethod;
            set => SetProperty(ref _fitMethod, value);
        }

        private double _ransacThreshold = 5.0;
        public double RansacThreshold
        {
            get => _ransacThreshold;
            set => SetProperty(ref _ransacThreshold, Math.Max(0.1, value));
        }

        private int _minFoundCalipers = 3;
        public int MinFoundCalipers
        {
            get => _minFoundCalipers;
            set => SetProperty(ref _minFoundCalipers, Math.Max(3, value));
        }

        public CircleFitTool()
        {
            Name = "Circle Fit";
            ToolType = "CircleFitTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                Mat workImage = GetROIImage(inputImage);
                Mat grayImage = new Mat();

                if (workImage.Channels() > 1)
                    Cv2.CvtColor(workImage, grayImage, ColorConversionCodes.BGR2GRAY);
                else
                    grayImage = workImage.Clone();

                // 결과 이미지 생성
                Mat overlayImage = inputImage.Clone();
                if (overlayImage.Channels() == 1)
                    Cv2.CvtColor(overlayImage, overlayImage, ColorConversionCodes.GRAY2BGR);

                // 예상 원 표시
                Cv2.Circle(overlayImage,
                    new Point((int)CenterPoint.X, (int)CenterPoint.Y),
                    (int)ExpectedRadius,
                    new Scalar(128, 128, 128), 1);

                // 각 Caliper 위치에서 Edge 검출
                var foundPoints = new List<Point2d>();
                double angleRange = EndAngle - StartAngle;

                for (int i = 0; i < NumCalipers; i++)
                {
                    double angle = StartAngle + angleRange * i / NumCalipers;
                    double angleRad = angle * Math.PI / 180;

                    // 반경 방향의 검색 라인
                    double dirX = Math.Cos(angleRad);
                    double dirY = Math.Sin(angleRad);

                    var searchStart = new Point2d(
                        CenterPoint.X + dirX * (ExpectedRadius - SearchLength / 2),
                        CenterPoint.Y + dirY * (ExpectedRadius - SearchLength / 2));
                    var searchEnd = new Point2d(
                        CenterPoint.X + dirX * (ExpectedRadius + SearchLength / 2),
                        CenterPoint.Y + dirY * (ExpectedRadius + SearchLength / 2));

                    // Edge 검출
                    var edge = FindEdgeAlongLine(grayImage, searchStart, searchEnd, SearchWidth);

                    // Caliper 검색 영역 표시
                    Cv2.Line(overlayImage,
                        new Point((int)searchStart.X, (int)searchStart.Y),
                        new Point((int)searchEnd.X, (int)searchEnd.Y),
                        new Scalar(128, 128, 128), 1);

                    if (edge.HasValue)
                    {
                        foundPoints.Add(edge.Value.Point);

                        // 검출된 Edge 표시
                        Cv2.Circle(overlayImage,
                            new Point((int)edge.Value.Point.X, (int)edge.Value.Point.Y),
                            3, new Scalar(0, 255, 0), -1);
                    }
                }

                result.Data["FoundCount"] = foundPoints.Count;
                result.Data["TotalCalipers"] = NumCalipers;

                // 원 피팅
                if (foundPoints.Count >= MinFoundCalipers)
                {
                    CircleFitResult circleResult;

                    if (FitMethod == CircleFitMethod.RANSAC)
                    {
                        circleResult = FitCircleRANSAC(foundPoints);
                    }
                    else
                    {
                        circleResult = FitCircleLeastSquares(foundPoints);
                    }

                    if (circleResult.Success)
                    {
                        // 피팅된 원 그리기
                        Cv2.Circle(overlayImage,
                            new Point((int)circleResult.CenterX, (int)circleResult.CenterY),
                            (int)circleResult.Radius,
                            new Scalar(0, 255, 255), 2);

                        // 중심점 표시
                        Cv2.DrawMarker(overlayImage,
                            new Point((int)circleResult.CenterX, (int)circleResult.CenterY),
                            new Scalar(0, 0, 255), MarkerTypes.Cross, 20, 2);

                        // 결과 데이터
                        result.Data["CenterX"] = circleResult.CenterX;
                        result.Data["CenterY"] = circleResult.CenterY;
                        result.Data["Radius"] = circleResult.Radius;
                        result.Data["Diameter"] = circleResult.Radius * 2;
                        result.Data["FitError"] = circleResult.FitError;
                        result.Data["InlierCount"] = circleResult.InlierCount;

                        // Graphics 추가
                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Circle,
                            Position = new Point2d(circleResult.CenterX, circleResult.CenterY),
                            Radius = circleResult.Radius,
                            Color = new Scalar(0, 255, 255)
                        });

                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Crosshair,
                            Position = new Point2d(circleResult.CenterX, circleResult.CenterY),
                            Color = new Scalar(0, 0, 255)
                        });

                        result.Success = true;
                        result.Message = $"원 검출 완료: 중심({circleResult.CenterX:F1}, {circleResult.CenterY:F1}), R={circleResult.Radius:F1}";
                    }
                    else
                    {
                        result.Success = false;
                        result.Message = "원 피팅 실패";
                    }
                }
                else
                {
                    result.Success = false;
                    result.Message = $"검출된 점 부족: {foundPoints.Count}/{MinFoundCalipers} 필요";
                }

                result.OutputImage = grayImage;
                result.OverlayImage = overlayImage;

                if (workImage != inputImage)
                    workImage.Dispose();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Circle Fit 실행 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        private (Point2d Point, double Score)? FindEdgeAlongLine(Mat image, Point2d start, Point2d end, double width)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < 1)
                return null;

            double ux = dx / length;
            double uy = dy / length;
            double vx = -uy;
            double vy = ux;

            int profileLength = (int)length;
            var profile = new double[profileLength];

            // 프로파일 추출
            for (int i = 0; i < profileLength; i++)
            {
                double sum = 0;
                int count = 0;

                for (int j = -(int)(width / 2); j <= (int)(width / 2); j++)
                {
                    double px = start.X + ux * i + vx * j;
                    double py = start.Y + uy * i + vy * j;

                    int ix = (int)px;
                    int iy = (int)py;

                    if (ix >= 0 && ix < image.Width && iy >= 0 && iy < image.Height)
                    {
                        sum += image.At<byte>(iy, ix);
                        count++;
                    }
                }

                profile[i] = count > 0 ? sum / count : 0;
            }

            // 그래디언트 계산 및 Edge 검출
            double maxScore = 0;
            int maxPos = -1;

            for (int i = 2; i < profileLength - 2; i++)
            {
                double g = (-profile[i - 2] - profile[i - 1] + profile[i + 1] + profile[i + 2]) / 4;

                bool matchesPolarity = false;
                if (Polarity == EdgePolarity.Any)
                    matchesPolarity = Math.Abs(g) > EdgeThreshold;
                else if (Polarity == EdgePolarity.DarkToLight)
                    matchesPolarity = g > EdgeThreshold;
                else if (Polarity == EdgePolarity.LightToDark)
                    matchesPolarity = -g > EdgeThreshold;

                if (matchesPolarity && Math.Abs(g) > maxScore)
                {
                    maxScore = Math.Abs(g);
                    maxPos = i;
                }
            }

            if (maxPos >= 0)
            {
                return (new Point2d(start.X + ux * maxPos, start.Y + uy * maxPos), maxScore);
            }

            return null;
        }

        private CircleFitResult FitCircleLeastSquares(List<Point2d> points)
        {
            var result = new CircleFitResult();

            if (points.Count < 3)
            {
                result.Success = false;
                return result;
            }

            // Algebraic Circle Fit (Kasa method)
            double sumX = 0, sumY = 0, sumXX = 0, sumYY = 0, sumXY = 0;
            double sumXXX = 0, sumYYY = 0, sumXXY = 0, sumXYY = 0;
            int n = points.Count;

            foreach (var p in points)
            {
                double x = p.X;
                double y = p.Y;
                double xx = x * x;
                double yy = y * y;

                sumX += x;
                sumY += y;
                sumXX += xx;
                sumYY += yy;
                sumXY += x * y;
                sumXXX += xx * x;
                sumYYY += yy * y;
                sumXXY += xx * y;
                sumXYY += x * yy;
            }

            double A = n * sumXX - sumX * sumX;
            double B = n * sumXY - sumX * sumY;
            double C = n * sumYY - sumY * sumY;
            double D = 0.5 * (n * sumXXX + n * sumXYY - sumX * sumXX - sumX * sumYY);
            double E = 0.5 * (n * sumXXY + n * sumYYY - sumY * sumXX - sumY * sumYY);

            double denom = A * C - B * B;
            if (Math.Abs(denom) < 1e-10)
            {
                result.Success = false;
                return result;
            }

            double cx = (D * C - B * E) / denom;
            double cy = (A * E - B * D) / denom;

            // 반경 계산
            double sumR = 0;
            foreach (var p in points)
            {
                double dx = p.X - cx;
                double dy = p.Y - cy;
                sumR += Math.Sqrt(dx * dx + dy * dy);
            }
            double radius = sumR / n;

            // 피팅 오차 계산
            double totalError = 0;
            foreach (var p in points)
            {
                double dx = p.X - cx;
                double dy = p.Y - cy;
                double dist = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - radius);
                totalError += dist * dist;
            }

            result.CenterX = cx;
            result.CenterY = cy;
            result.Radius = radius;
            result.FitError = Math.Sqrt(totalError / n);
            result.InlierCount = n;
            result.Success = true;

            return result;
        }

        private CircleFitResult FitCircleRANSAC(List<Point2d> points)
        {
            var result = new CircleFitResult();

            if (points.Count < 3)
            {
                result.Success = false;
                return result;
            }

            int maxIterations = 100;
            int bestInlierCount = 0;
            List<Point2d> bestInliers = new List<Point2d>();

            var random = new Random();

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // 랜덤으로 3점 선택
                var indices = new HashSet<int>();
                while (indices.Count < 3)
                {
                    indices.Add(random.Next(points.Count));
                }

                var samplePoints = indices.Select(i => points[i]).ToList();

                // 3점으로 원 피팅
                var circle = FitCircleFrom3Points(samplePoints);
                if (!circle.Success)
                    continue;

                // Inlier 계산
                var inliers = new List<Point2d>();
                foreach (var p in points)
                {
                    double dx = p.X - circle.CenterX;
                    double dy = p.Y - circle.CenterY;
                    double dist = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - circle.Radius);
                    if (dist < RansacThreshold)
                        inliers.Add(p);
                }

                if (inliers.Count > bestInlierCount)
                {
                    bestInlierCount = inliers.Count;
                    bestInliers = inliers;
                }
            }

            if (bestInliers.Count >= MinFoundCalipers)
            {
                // Inlier로 최종 피팅
                result = FitCircleLeastSquares(bestInliers);
                result.InlierCount = bestInlierCount;
            }
            else
            {
                result.Success = false;
            }

            return result;
        }

        private CircleFitResult FitCircleFrom3Points(List<Point2d> points)
        {
            var result = new CircleFitResult();

            if (points.Count != 3)
            {
                result.Success = false;
                return result;
            }

            var p1 = points[0];
            var p2 = points[1];
            var p3 = points[2];

            double ax = p1.X, ay = p1.Y;
            double bx = p2.X, by = p2.Y;
            double cx = p3.X, cy = p3.Y;

            double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (Math.Abs(d) < 1e-10)
            {
                result.Success = false;
                return result;
            }

            double ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
            double uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;

            result.CenterX = ux;
            result.CenterY = uy;
            result.Radius = Math.Sqrt((ax - ux) * (ax - ux) + (ay - uy) * (ay - uy));
            result.Success = true;

            return result;
        }

        public override VisionToolBase Clone()
        {
            return new CircleFitTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                CenterPoint = this.CenterPoint,
                ExpectedRadius = this.ExpectedRadius,
                NumCalipers = this.NumCalipers,
                SearchLength = this.SearchLength,
                SearchWidth = this.SearchWidth,
                StartAngle = this.StartAngle,
                EndAngle = this.EndAngle,
                Polarity = this.Polarity,
                EdgeThreshold = this.EdgeThreshold,
                FitMethod = this.FitMethod,
                RansacThreshold = this.RansacThreshold,
                MinFoundCalipers = this.MinFoundCalipers
            };
        }
    }

    public class CircleFitResult
    {
        public bool Success { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Radius { get; set; }
        public double FitError { get; set; }
        public int InlierCount { get; set; }
    }

    public enum CircleFitMethod
    {
        LeastSquares,
        RANSAC
    }
}
