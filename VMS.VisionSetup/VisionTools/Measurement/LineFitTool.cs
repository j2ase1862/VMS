using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VMS.VisionSetup.VisionTools.Measurement
{
    /// <summary>
    /// 직선 검출 및 피팅 도구 (Cognex VisionPro CogFindLineTool 대체)
    /// 여러 Caliper를 사용하여 직선을 검출하고 피팅
    /// </summary>
    public class LineFitTool : VisionToolBase
    {
        // 검색 영역 설정
        private Point2d _startPoint = new Point2d(0, 100);
        public Point2d StartPoint
        {
            get => _startPoint;
            set => SetProperty(ref _startPoint, value);
        }

        private Point2d _endPoint = new Point2d(200, 100);
        public Point2d EndPoint
        {
            get => _endPoint;
            set => SetProperty(ref _endPoint, value);
        }

        private int _numCalipers = 10;
        public int NumCalipers
        {
            get => _numCalipers;
            set => SetProperty(ref _numCalipers, Math.Max(2, value));
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
        private LineFitMethod _fitMethod = LineFitMethod.LeastSquares;
        public LineFitMethod FitMethod
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
            set => SetProperty(ref _minFoundCalipers, Math.Max(2, value));
        }

        public LineFitTool()
        {
            Name = "Line Fit";
            ToolType = "LineFitTool";
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

                // 기준선 벡터 계산
                double dx = EndPoint.X - StartPoint.X;
                double dy = EndPoint.Y - StartPoint.Y;
                double baseLength = Math.Sqrt(dx * dx + dy * dy);

                if (baseLength < 1)
                {
                    result.Success = false;
                    result.Message = "기준선이 너무 짧습니다.";
                    return result;
                }

                // 단위 벡터 (기준선 방향)
                double ux = dx / baseLength;
                double uy = dy / baseLength;

                // 수직 벡터 (검색 방향)
                double vx = -uy;
                double vy = ux;

                // 결과 이미지 생성
                Mat overlayImage = inputImage.Clone();
                if (overlayImage.Channels() == 1)
                    Cv2.CvtColor(overlayImage, overlayImage, ColorConversionCodes.GRAY2BGR);

                // Caliper 위치에서 Edge 검출
                var foundPoints = new List<Point2d>();
                var caliperResults = new List<CaliperResult>();

                for (int i = 0; i < NumCalipers; i++)
                {
                    // Caliper 중심 위치
                    double t = (double)i / (NumCalipers - 1);
                    double cx = StartPoint.X + dx * t;
                    double cy = StartPoint.Y + dy * t;

                    // Caliper 검색 영역 (수직 방향)
                    var searchStart = new Point2d(cx - vx * SearchLength / 2, cy - vy * SearchLength / 2);
                    var searchEnd = new Point2d(cx + vx * SearchLength / 2, cy + vy * SearchLength / 2);

                    // Edge 검출
                    var edge = FindEdgeAlongLine(grayImage, searchStart, searchEnd, SearchWidth);

                    var caliperResult = new CaliperResult
                    {
                        Index = i,
                        SearchStart = searchStart,
                        SearchEnd = searchEnd,
                        Found = edge.HasValue
                    };

                    // Caliper 검색 영역 표시
                    Cv2.Line(overlayImage,
                        new Point((int)searchStart.X, (int)searchStart.Y),
                        new Point((int)searchEnd.X, (int)searchEnd.Y),
                        new Scalar(128, 128, 128), 1);

                    if (edge.HasValue)
                    {
                        foundPoints.Add(edge.Value.Point);
                        caliperResult.EdgePoint = edge.Value.Point;
                        caliperResult.EdgeScore = edge.Value.Score;

                        // 검출된 Edge 표시
                        Cv2.Circle(overlayImage,
                            new Point((int)edge.Value.Point.X, (int)edge.Value.Point.Y),
                            3, new Scalar(0, 255, 0), -1);
                    }
                    else
                    {
                        // 미검출 표시
                        Cv2.Circle(overlayImage,
                            new Point((int)cx, (int)cy),
                            3, new Scalar(0, 0, 255), -1);
                    }

                    caliperResults.Add(caliperResult);
                }

                result.Data["CaliperResults"] = caliperResults;
                result.Data["FoundCount"] = foundPoints.Count;
                result.Data["TotalCalipers"] = NumCalipers;

                // 직선 피팅
                if (foundPoints.Count >= MinFoundCalipers)
                {
                    LineFitResult lineResult;

                    if (FitMethod == LineFitMethod.RANSAC)
                    {
                        lineResult = FitLineRANSAC(foundPoints);
                    }
                    else
                    {
                        lineResult = FitLineLeastSquares(foundPoints);
                    }

                    if (lineResult.Success)
                    {
                        // 피팅된 직선 그리기
                        var linePoints = GetLineEndPoints(lineResult, inputImage.Width, inputImage.Height);
                        Cv2.Line(overlayImage,
                            new Point((int)linePoints.Item1.X, (int)linePoints.Item1.Y),
                            new Point((int)linePoints.Item2.X, (int)linePoints.Item2.Y),
                            new Scalar(0, 255, 255), 2);

                        // 결과 데이터
                        result.Data["LineAngle"] = lineResult.Angle;
                        result.Data["LinePointX"] = lineResult.PointOnLine.X;
                        result.Data["LinePointY"] = lineResult.PointOnLine.Y;
                        result.Data["DirectionX"] = lineResult.Direction.X;
                        result.Data["DirectionY"] = lineResult.Direction.Y;
                        result.Data["FitError"] = lineResult.FitError;
                        result.Data["InlierCount"] = lineResult.InlierCount;

                        // Graphics 추가
                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Line,
                            Position = linePoints.Item1,
                            EndPosition = linePoints.Item2,
                            Color = new Scalar(0, 255, 255)
                        });

                        result.Success = true;
                        result.Message = $"직선 검출 완료: 각도 {lineResult.Angle:F2}°, {foundPoints.Count}/{NumCalipers} 점 사용";
                    }
                    else
                    {
                        result.Success = false;
                        result.Message = "직선 피팅 실패";
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
                result.Message = $"Line Fit 실행 실패: {ex.Message}";
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
            EdgePolarity foundPolarity = EdgePolarity.DarkToLight;

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
                    foundPolarity = g > 0 ? EdgePolarity.DarkToLight : EdgePolarity.LightToDark;
                }
            }

            if (maxPos >= 0)
            {
                return (new Point2d(start.X + ux * maxPos, start.Y + uy * maxPos), maxScore);
            }

            return null;
        }

        private LineFitResult FitLineLeastSquares(List<Point2d> points)
        {
            var result = new LineFitResult();

            if (points.Count < 2)
            {
                result.Success = false;
                return result;
            }

            // OpenCV FitLine 사용
            var pointsArray = points.Select(p => new Point2f((float)p.X, (float)p.Y)).ToArray();
            using var line = new Mat();

            Cv2.FitLine(
                InputArray.Create(pointsArray),
                line,
                DistanceTypes.L2, 0, 0.01, 0.01);

            // Mat에서 결과 추출
            line.GetArray(out float[] lineParams);

            result.Direction = new Point2d(lineParams[0], lineParams[1]);
            result.PointOnLine = new Point2d(lineParams[2], lineParams[3]);
            result.Angle = Math.Atan2(lineParams[1], lineParams[0]) * 180 / Math.PI;

            // 피팅 오차 계산
            double totalError = 0;
            foreach (var p in points)
            {
                double dist = DistanceToLine(p, result.PointOnLine, result.Direction);
                totalError += dist * dist;
            }
            result.FitError = Math.Sqrt(totalError / points.Count);
            result.InlierCount = points.Count;
            result.Success = true;

            return result;
        }

        private LineFitResult FitLineRANSAC(List<Point2d> points)
        {
            var result = new LineFitResult();

            if (points.Count < 2)
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
                // 랜덤으로 2점 선택
                int i1 = random.Next(points.Count);
                int i2 = random.Next(points.Count);
                while (i2 == i1)
                    i2 = random.Next(points.Count);

                var p1 = points[i1];
                var p2 = points[i2];

                // 직선 파라미터 계산
                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001) continue;

                var dir = new Point2d(dx / len, dy / len);

                // Inlier 계산
                var inliers = new List<Point2d>();
                foreach (var p in points)
                {
                    double dist = DistanceToLine(p, p1, dir);
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
                result = FitLineLeastSquares(bestInliers);
                result.InlierCount = bestInlierCount;
            }
            else
            {
                result.Success = false;
            }

            return result;
        }

        private double DistanceToLine(Point2d point, Point2d linePoint, Point2d lineDir)
        {
            double dx = point.X - linePoint.X;
            double dy = point.Y - linePoint.Y;

            // 직선까지의 수직 거리
            double cross = Math.Abs(dx * lineDir.Y - dy * lineDir.X);
            return cross;
        }

        private (Point2d, Point2d) GetLineEndPoints(LineFitResult line, int imageWidth, int imageHeight)
        {
            // 이미지 경계와의 교차점 계산
            double t1 = -1000;
            double t2 = 1000;

            var p1 = new Point2d(
                line.PointOnLine.X + line.Direction.X * t1,
                line.PointOnLine.Y + line.Direction.Y * t1);
            var p2 = new Point2d(
                line.PointOnLine.X + line.Direction.X * t2,
                line.PointOnLine.Y + line.Direction.Y * t2);

            return (p1, p2);
        }

        public override VisionToolBase Clone()
        {
            return new LineFitTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                StartPoint = this.StartPoint,
                EndPoint = this.EndPoint,
                NumCalipers = this.NumCalipers,
                SearchLength = this.SearchLength,
                SearchWidth = this.SearchWidth,
                Polarity = this.Polarity,
                EdgeThreshold = this.EdgeThreshold,
                FitMethod = this.FitMethod,
                RansacThreshold = this.RansacThreshold,
                MinFoundCalipers = this.MinFoundCalipers
            };
        }
    }

    public class CaliperResult
    {
        public int Index { get; set; }
        public Point2d SearchStart { get; set; }
        public Point2d SearchEnd { get; set; }
        public bool Found { get; set; }
        public Point2d EdgePoint { get; set; }
        public double EdgeScore { get; set; }
    }

    public class LineFitResult
    {
        public bool Success { get; set; }
        public Point2d PointOnLine { get; set; }
        public Point2d Direction { get; set; }
        public double Angle { get; set; }
        public double FitError { get; set; }
        public int InlierCount { get; set; }
    }

    public enum LineFitMethod
    {
        LeastSquares,
        RANSAC
    }
}
