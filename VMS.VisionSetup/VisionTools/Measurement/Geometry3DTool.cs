using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace VMS.VisionSetup.VisionTools.Measurement
{
    /// <summary>
    /// 3D 기하 연산 종류
    /// </summary>
    public enum Geometry3DOperation
    {
        /// <summary>두 3D 점 사이의 유클리드 거리</summary>
        PointToPointDistance,

        /// <summary>3D 점에서 평면까지의 수직 거리</summary>
        PointToPlaneDistance,

        /// <summary>두 평면 사이의 각도 (법선 벡터 내적)</summary>
        PlaneToPlaneAngle,

        /// <summary>두 평면 사이의 거리 (평행 평면 간)</summary>
        PlaneToPlaneDistance,

        /// <summary>3D 점에서 직선까지의 수직 거리</summary>
        PointToLineDistance3D
    }

    /// <summary>
    /// 상위 도구에서 추출된 3D 기하 요소
    /// </summary>
    public class SourceGeometry3D
    {
        public string ToolId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;

        /// <summary>점 (X, Y, Z in mm)</summary>
        public Vector3 Point { get; set; }

        /// <summary>평면 방정식 계수 (ax + by + cz + d = 0), 법선 정규화됨</summary>
        public double PlaneA { get; set; }
        public double PlaneB { get; set; }
        public double PlaneC { get; set; }
        public double PlaneD { get; set; }

        /// <summary>평면 법선 벡터</summary>
        public Vector3 Normal => new Vector3((float)PlaneA, (float)PlaneB, (float)PlaneC);

        /// <summary>직선 (점 + 방향 벡터)</summary>
        public Vector3 LinePoint { get; set; }
        public Vector3 LineDirection { get; set; }

        /// <summary>데이터 유효성</summary>
        public bool HasPoint { get; set; }
        public bool HasPlane { get; set; }
        public bool HasLine { get; set; }
    }

    /// <summary>
    /// 3D 기하 측정 도구 (Phase 5)
    /// 점-점 거리, 점-평면 거리, 평면-평면 각도/거리 등 3D 공간 측정
    /// PlaneFitTool, CaliperTool+HeightMap 등에서 추출한 3D 기하 요소 간 관계 계산
    /// </summary>
    public class Geometry3DTool : VisionToolBase
    {
        private Geometry3DOperation _operation = Geometry3DOperation.PointToPointDistance;
        public Geometry3DOperation Operation
        {
            get => _operation;
            set => SetProperty(ref _operation, value);
        }

        /// <summary>
        /// 소스 기하 요소 목록 (VisionService가 Execute 전에 채워줌)
        /// [0] = Source A, [1] = Source B
        /// </summary>
        public List<SourceGeometry3D> SourceGeometries { get; } = new();

        /// <summary>
        /// 수동 입력 포인트 A (2D 픽셀 좌표, HeightMapMetadata에서 3D 복원)
        /// </summary>
        private Point2d _pointA = new(0, 0);
        public Point2d PointA
        {
            get => _pointA;
            set => SetProperty(ref _pointA, value);
        }

        /// <summary>수동 입력 포인트 B</summary>
        private Point2d _pointB = new(100, 100);
        public Point2d PointB
        {
            get => _pointB;
            set => SetProperty(ref _pointB, value);
        }

        /// <summary>수동 입력 모드 사용 여부</summary>
        private bool _useManualPoints = true;
        public bool UseManualPoints
        {
            get => _useManualPoints;
            set => SetProperty(ref _useManualPoints, value);
        }

        public Geometry3DTool()
        {
            Name = "3D Geometry";
            ToolType = "Geometry3DTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                var metadata = VisionService.Instance.CurrentHeightMapMetadata;

                switch (Operation)
                {
                    case Geometry3DOperation.PointToPointDistance:
                        ExecutePointToPoint(result, metadata);
                        break;
                    case Geometry3DOperation.PointToPlaneDistance:
                        ExecutePointToPlane(result, metadata);
                        break;
                    case Geometry3DOperation.PlaneToPlaneAngle:
                        ExecutePlaneToPlaneAngle(result);
                        break;
                    case Geometry3DOperation.PlaneToPlaneDistance:
                        ExecutePlaneToPlaneDistance(result);
                        break;
                    case Geometry3DOperation.PointToLineDistance3D:
                        ExecutePointToLine3D(result, metadata);
                        break;
                }

                // 그래픽 오버레이 추가
                if (result.Success)
                {
                    AddMeasurementGraphics(result, inputImage, metadata);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"3D 측정 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        #region Measurement Operations

        private void ExecutePointToPoint(VisionResult result, HeightMapMetadata? metadata)
        {
            Vector3? p1, p2;

            if (UseManualPoints && metadata != null)
            {
                p1 = Get3DPointFromPixel(metadata, PointA);
                p2 = Get3DPointFromPixel(metadata, PointB);
            }
            else if (SourceGeometries.Count >= 2)
            {
                p1 = SourceGeometries[0].HasPoint ? SourceGeometries[0].Point : null;
                p2 = SourceGeometries[1].HasPoint ? SourceGeometries[1].Point : null;
            }
            else
            {
                result.Success = false;
                result.Message = "두 개의 3D 포인트가 필요합니다";
                return;
            }

            if (!p1.HasValue || !p2.HasValue)
            {
                result.Success = false;
                result.Message = "유효한 3D 좌표를 얻을 수 없습니다";
                return;
            }

            double distance = Vector3.Distance(p1.Value, p2.Value);
            double dx = p2.Value.X - p1.Value.X;
            double dy = p2.Value.Y - p1.Value.Y;
            double dz = p2.Value.Z - p1.Value.Z;

            result.Success = true;
            result.Data["Distance3D"] = distance;
            result.Data["DeltaX"] = dx;
            result.Data["DeltaY"] = dy;
            result.Data["DeltaZ"] = dz;
            result.Data["Point1X"] = (double)p1.Value.X;
            result.Data["Point1Y"] = (double)p1.Value.Y;
            result.Data["Point1Z"] = (double)p1.Value.Z;
            result.Data["Point2X"] = (double)p2.Value.X;
            result.Data["Point2Y"] = (double)p2.Value.Y;
            result.Data["Point2Z"] = (double)p2.Value.Z;
            result.Message = $"3D 거리: {distance:F3}mm (ΔX={dx:F3}, ΔY={dy:F3}, ΔZ={dz:F3})";
        }

        private void ExecutePointToPlane(VisionResult result, HeightMapMetadata? metadata)
        {
            Vector3? point = null;
            double a = 0, b = 0, c = 0, d = 0;
            bool hasPlane = false;

            // 포인트 획득
            if (UseManualPoints && metadata != null)
            {
                point = Get3DPointFromPixel(metadata, PointA);
            }
            else if (SourceGeometries.Count >= 1 && SourceGeometries[0].HasPoint)
            {
                point = SourceGeometries[0].Point;
            }

            // 평면 획득 (SourceGeometries에서)
            foreach (var src in SourceGeometries)
            {
                if (src.HasPlane)
                {
                    a = src.PlaneA; b = src.PlaneB; c = src.PlaneC; d = src.PlaneD;
                    hasPlane = true;
                    break;
                }
            }

            if (!point.HasValue)
            {
                result.Success = false;
                result.Message = "유효한 3D 포인트가 필요합니다";
                return;
            }

            if (!hasPlane)
            {
                result.Success = false;
                result.Message = "평면 데이터가 필요합니다 (PlaneFitTool 연결 필요)";
                return;
            }

            // 점-평면 거리: |ax + by + cz + d| / sqrt(a² + b² + c²)
            double norm = Math.Sqrt(a * a + b * b + c * c);
            double signedDist = (a * point.Value.X + b * point.Value.Y + c * point.Value.Z + d) / norm;
            double distance = Math.Abs(signedDist);

            result.Success = true;
            result.Data["Distance3D"] = distance;
            result.Data["SignedDistance"] = signedDist;
            result.Data["PointX"] = (double)point.Value.X;
            result.Data["PointY"] = (double)point.Value.Y;
            result.Data["PointZ"] = (double)point.Value.Z;
            result.Message = $"점-평면 거리: {distance:F3}mm (부호: {signedDist:F3}mm)";
        }

        private void ExecutePlaneToPlaneAngle(VisionResult result)
        {
            if (SourceGeometries.Count < 2 || !SourceGeometries[0].HasPlane || !SourceGeometries[1].HasPlane)
            {
                result.Success = false;
                result.Message = "두 개의 평면 데이터가 필요합니다 (PlaneFitTool 2개 연결)";
                return;
            }

            var n1 = SourceGeometries[0].Normal;
            var n2 = SourceGeometries[1].Normal;

            // 두 법선 벡터 사이각: cos(θ) = |n1 · n2| / (|n1| × |n2|)
            double dot = Math.Abs(Vector3.Dot(n1, n2));
            dot = Math.Min(1.0, dot); // clamp for acos safety
            double angleRad = Math.Acos(dot);
            double angleDeg = angleRad * 180.0 / Math.PI;

            result.Success = true;
            result.Data["AngleDeg"] = angleDeg;
            result.Data["AngleRad"] = angleRad;
            result.Data["Normal1X"] = (double)n1.X;
            result.Data["Normal1Y"] = (double)n1.Y;
            result.Data["Normal1Z"] = (double)n1.Z;
            result.Data["Normal2X"] = (double)n2.X;
            result.Data["Normal2Y"] = (double)n2.Y;
            result.Data["Normal2Z"] = (double)n2.Z;
            result.Message = $"평면-평면 각도: {angleDeg:F3}°";
        }

        private void ExecutePlaneToPlaneDistance(VisionResult result)
        {
            if (SourceGeometries.Count < 2 || !SourceGeometries[0].HasPlane || !SourceGeometries[1].HasPlane)
            {
                result.Success = false;
                result.Message = "두 개의 평면 데이터가 필요합니다";
                return;
            }

            var s0 = SourceGeometries[0];
            var s1 = SourceGeometries[1];

            // 평행 평면 간 거리: |d1 - d2| / |n| (법선이 같은 방향이라 가정)
            // 비평행이면 법선 방향 일치시킨 후 계산
            var n1 = s0.Normal;
            var n2 = s1.Normal;

            double dot = Vector3.Dot(n1, n2);
            double d1 = s0.PlaneD;
            double d2 = s1.PlaneD;

            // 법선 방향이 반대면 부호 반전
            if (dot < 0) d2 = -d2;

            double norm1 = n1.Length();
            double distance = Math.Abs(d1 / norm1 - d2 / n2.Length());

            result.Success = true;
            result.Data["Distance3D"] = distance;
            result.Data["IsParallel"] = Math.Abs(Math.Abs(dot) - 1.0) < 0.01;
            result.Message = $"평면-평면 거리: {distance:F3}mm (평행: {Math.Abs(Math.Abs(dot) - 1.0) < 0.01})";
        }

        private void ExecutePointToLine3D(VisionResult result, HeightMapMetadata? metadata)
        {
            Vector3? point = null;

            if (UseManualPoints && metadata != null)
                point = Get3DPointFromPixel(metadata, PointA);
            else if (SourceGeometries.Count >= 1 && SourceGeometries[0].HasPoint)
                point = SourceGeometries[0].Point;

            SourceGeometry3D? lineSrc = SourceGeometries.FirstOrDefault(s => s.HasLine);

            if (!point.HasValue || lineSrc == null)
            {
                result.Success = false;
                result.Message = "3D 포인트와 직선 데이터가 필요합니다";
                return;
            }

            // 점-직선 거리: ||(P - Q) × d|| / ||d||
            var pq = point.Value - lineSrc.LinePoint;
            var cross = Vector3.Cross(pq, lineSrc.LineDirection);
            double distance = cross.Length() / lineSrc.LineDirection.Length();

            result.Success = true;
            result.Data["Distance3D"] = distance;
            result.Message = $"점-직선 거리: {distance:F3}mm";
        }

        #endregion

        #region Helpers

        private static Vector3? Get3DPointFromPixel(HeightMapMetadata metadata, Point2d pixel)
        {
            int u = (int)Math.Round(pixel.X);
            int v = (int)Math.Round(pixel.Y);
            return metadata.GetPoint3D(u, v);
        }

        private void AddMeasurementGraphics(VisionResult result, Mat inputImage, HeightMapMetadata? metadata)
        {
            switch (Operation)
            {
                case Geometry3DOperation.PointToPointDistance:
                    if (UseManualPoints)
                    {
                        // 두 점 사이 라인 + 거리 텍스트
                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Crosshair,
                            Position = PointA,
                            Width = 20, Height = 20,
                            Color = new Scalar(0, 255, 0),
                            Thickness = 2
                        });
                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Crosshair,
                            Position = PointB,
                            Width = 20, Height = 20,
                            Color = new Scalar(255, 0, 0),
                            Thickness = 2
                        });
                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Line,
                            Position = PointA,
                            EndPosition = PointB,
                            Color = new Scalar(0, 255, 255),
                            Thickness = 2
                        });

                        var midPoint = new Point2d(
                            (PointA.X + PointB.X) / 2,
                            (PointA.Y + PointB.Y) / 2 - 15);
                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Text,
                            Position = midPoint,
                            Text = $"{result.Data["Distance3D"]:F3}mm",
                            Color = new Scalar(0, 255, 255)
                        });
                    }
                    break;

                case Geometry3DOperation.PointToPlaneDistance:
                    if (UseManualPoints)
                    {
                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Crosshair,
                            Position = PointA,
                            Width = 20, Height = 20,
                            Color = new Scalar(0, 255, 0),
                            Thickness = 2
                        });
                        result.Graphics.Add(new GraphicOverlay
                        {
                            Type = GraphicType.Text,
                            Position = new Point2d(PointA.X + 15, PointA.Y - 10),
                            Text = $"→Plane: {result.Data["Distance3D"]:F3}mm",
                            Color = new Scalar(0, 200, 255)
                        });
                    }
                    break;

                case Geometry3DOperation.PlaneToPlaneAngle:
                case Geometry3DOperation.PlaneToPlaneDistance:
                    // 텍스트만 표시
                    result.Graphics.Add(new GraphicOverlay
                    {
                        Type = GraphicType.Text,
                        Position = new Point2d(10, 30),
                        Text = result.Message,
                        Color = new Scalar(0, 200, 255)
                    });
                    break;
            }
        }

        #endregion

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "Distance3D", "SignedDistance",
                "DeltaX", "DeltaY", "DeltaZ",
                "AngleDeg", "AngleRad",
                "Point1X", "Point1Y", "Point1Z",
                "Point2X", "Point2Y", "Point2Z",
                "IsParallel"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new Geometry3DTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                Operation = this.Operation,
                PointA = this.PointA,
                PointB = this.PointB,
                UseManualPoints = this.UseManualPoints,
                IsEnabled = this.IsEnabled,
                UseROI = this.UseROI,
                ROI = this.ROI
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
