using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.Measurement
{
    /// <summary>
    /// 기하 요소 타입
    /// </summary>
    public enum GeometryType
    {
        Point,
        Line,
        Circle
    }

    /// <summary>
    /// 기하 연산 종류
    /// </summary>
    public enum GeometryOperation
    {
        /// <summary>점-점 거리</summary>
        PointPointDistance,
        /// <summary>점-직선 수직 거리</summary>
        PointLineDistance,
        /// <summary>두 직선 사이의 거리 (평행 직선 간 수직 거리)</summary>
        LineLineDistance,
        /// <summary>두 직선 사이의 각도</summary>
        LineLineAngle,
        /// <summary>두 직선의 교차점</summary>
        LineLineIntersection,
        /// <summary>직선과 원의 교차점</summary>
        LineCircleIntersection,
        /// <summary>두 원의 중심 간 거리</summary>
        CircleCircleDistance
    }

    /// <summary>
    /// 상위 도구에서 추출된 기하 요소
    /// </summary>
    public class SourceGeometry
    {
        public string ToolId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public GeometryType Type { get; set; }

        // Point 데이터
        public double X { get; set; }
        public double Y { get; set; }

        // Line 데이터 (점 + 방향벡터)
        public double DirectionX { get; set; }
        public double DirectionY { get; set; }
        public double Angle { get; set; }

        // Circle 데이터
        public double Radius { get; set; }
    }

    /// <summary>
    /// GeometryTool — 검출된 기하 요소(점, 직선, 원) 사이의 관계를 계산합니다.
    /// Cognex CogIntersectLineLineTool, CogDistancePointLineTool 등의 대체
    /// </summary>
    public class GeometryTool : VisionToolBase
    {
        private GeometryOperation _operation = GeometryOperation.PointPointDistance;
        public GeometryOperation Operation
        {
            get => _operation;
            set => SetProperty(ref _operation, value);
        }

        /// <summary>
        /// VisionService가 Execute 전에 채워주는 소스 기하 요소 목록
        /// </summary>
        public List<SourceGeometry> SourceGeometries { get; } = new();

        public GeometryTool()
        {
            Name = "Geometry";
            ToolType = "GeometryTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var sw = Stopwatch.StartNew();
            var result = new VisionResult();

            try
            {
                if (SourceGeometries.Count < 2)
                {
                    result.Success = false;
                    result.Message = $"2개 이상의 기하 요소가 필요합니다. (현재 {SourceGeometries.Count}개 연결됨)";
                    sw.Stop();
                    ExecutionTime = sw.Elapsed.TotalMilliseconds;
                    LastResult = result;
                    return result;
                }

                var geoA = SourceGeometries[0];
                var geoB = SourceGeometries[1];

                // 결과 이미지에 기하 요소 시각화
                Mat overlayImage = GetColorOverlayBase(inputImage);
                DrawSourceGeometry(overlayImage, geoA, new Scalar(0, 255, 255));  // Yellow
                DrawSourceGeometry(overlayImage, geoB, new Scalar(255, 128, 0));  // Blue-ish

                switch (Operation)
                {
                    case GeometryOperation.PointPointDistance:
                        ComputePointPointDistance(geoA, geoB, result, overlayImage);
                        break;
                    case GeometryOperation.PointLineDistance:
                        ComputePointLineDistance(geoA, geoB, result, overlayImage);
                        break;
                    case GeometryOperation.LineLineDistance:
                        ComputeLineLineDistance(geoA, geoB, result, overlayImage);
                        break;
                    case GeometryOperation.LineLineAngle:
                        ComputeLineLineAngle(geoA, geoB, result, overlayImage);
                        break;
                    case GeometryOperation.LineLineIntersection:
                        ComputeLineLineIntersection(geoA, geoB, result, overlayImage);
                        break;
                    case GeometryOperation.LineCircleIntersection:
                        ComputeLineCircleIntersection(geoA, geoB, result, overlayImage);
                        break;
                    case GeometryOperation.CircleCircleDistance:
                        ComputeCircleCircleDistance(geoA, geoB, result, overlayImage);
                        break;
                }

                result.OverlayImage = overlayImage;
                result.OutputImage = inputImage.Clone();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Geometry 연산 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        #region Geometry Operations

        private void ComputePointPointDistance(SourceGeometry a, SourceGeometry b,
            VisionResult result, Mat overlay)
        {
            var ptA = EnsurePoint(a);
            var ptB = EnsurePoint(b);
            if (ptA == null || ptB == null)
            {
                result.Success = false;
                result.Message = "PointPointDistance: 두 Point 요소가 필요합니다.";
                return;
            }

            double dx = ptB.Value.X - ptA.Value.X;
            double dy = ptB.Value.Y - ptA.Value.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            result.Data["Distance"] = distance;
            result.Data["DeltaX"] = dx;
            result.Data["DeltaY"] = dy;
            result.Data["PointAX"] = ptA.Value.X;
            result.Data["PointAY"] = ptA.Value.Y;
            result.Data["PointBX"] = ptB.Value.X;
            result.Data["PointBY"] = ptB.Value.Y;

            // 시각화: 두 점 사이 거리선
            Cv2.Line(overlay,
                new Point((int)ptA.Value.X, (int)ptA.Value.Y),
                new Point((int)ptB.Value.X, (int)ptB.Value.Y),
                new Scalar(0, 255, 0), 2);

            var mid = new Point(
                (int)((ptA.Value.X + ptB.Value.X) / 2),
                (int)((ptA.Value.Y + ptB.Value.Y) / 2));
            Cv2.PutText(overlay, $"{distance:F2}px",
                new Point(mid.X + 5, mid.Y - 5),
                HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 255), 1);

            result.Success = true;
            result.Message = $"거리: {distance:F2}px";
        }

        private void ComputePointLineDistance(SourceGeometry a, SourceGeometry b,
            VisionResult result, Mat overlay)
        {
            // a=Point, b=Line 또는 a=Line, b=Point
            SourceGeometry? pointSrc, lineSrc;
            if (a.Type == GeometryType.Point && b.Type == GeometryType.Line)
            { pointSrc = a; lineSrc = b; }
            else if (a.Type == GeometryType.Line && b.Type == GeometryType.Point)
            { pointSrc = b; lineSrc = a; }
            else
            {
                result.Success = false;
                result.Message = "PointLineDistance: Point 1개와 Line 1개가 필요합니다.";
                return;
            }

            // 점에서 직선까지 수직 거리
            // 직선: P0 + t*D, 점: Q
            // 수직 거리 = ||(Q - P0) × D|| / ||D||
            double qx = pointSrc.X, qy = pointSrc.Y;
            double px = lineSrc.X, py = lineSrc.Y;
            double dx = lineSrc.DirectionX, dy = lineSrc.DirectionY;
            double dirLen = Math.Sqrt(dx * dx + dy * dy);
            if (dirLen < 1e-12) { result.Success = false; result.Message = "유효하지 않은 직선."; return; }
            dx /= dirLen; dy /= dirLen;

            // 2D cross product: (Q-P) × D = (qx-px)*dy - (qy-py)*dx
            double cross = (qx - px) * dy - (qy - py) * dx;
            double distance = Math.Abs(cross);

            // 수선의 발: P0 + t*D, t = (Q-P)·D
            double t = (qx - px) * dx + (qy - py) * dy;
            double footX = px + t * dx;
            double footY = py + t * dy;

            result.Data["Distance"] = distance;
            result.Data["FootX"] = footX;
            result.Data["FootY"] = footY;
            result.Data["PointX"] = qx;
            result.Data["PointY"] = qy;
            result.Data["SignedDistance"] = cross;

            // 시각화: 수선
            Cv2.Line(overlay,
                new Point((int)qx, (int)qy),
                new Point((int)footX, (int)footY),
                new Scalar(0, 255, 0), 2);
            Cv2.Circle(overlay, new Point((int)footX, (int)footY), 4, new Scalar(0, 255, 0), -1);

            var mid = new Point((int)((qx + footX) / 2) + 5, (int)((qy + footY) / 2) - 5);
            Cv2.PutText(overlay, $"{distance:F2}px", mid,
                HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 255), 1);

            result.Success = true;
            result.Message = $"수직 거리: {distance:F2}px";
        }

        private void ComputeLineLineDistance(SourceGeometry a, SourceGeometry b,
            VisionResult result, Mat overlay)
        {
            if (a.Type != GeometryType.Line || b.Type != GeometryType.Line)
            {
                result.Success = false;
                result.Message = "LineLineDistance: 두 Line 요소가 필요합니다.";
                return;
            }

            // 직선 A의 방향벡터를 기준으로 B의 기준점에서 A까지 수직 거리 계산
            double dx = a.DirectionX, dy = a.DirectionY;
            double dirLen = Math.Sqrt(dx * dx + dy * dy);
            if (dirLen < 1e-12) { result.Success = false; result.Message = "유효하지 않은 직선."; return; }
            dx /= dirLen; dy /= dirLen;

            // B의 기준점에서 A 직선까지의 수직 거리
            double cross = (b.X - a.X) * dy - (b.Y - a.Y) * dx;
            double distance = Math.Abs(cross);

            // A 직선 위에서 B 기준점에 가장 가까운 점 (수선의 발)
            double t = (b.X - a.X) * dx + (b.Y - a.Y) * dy;
            double footAX = a.X + t * dx;
            double footAY = a.Y + t * dy;

            // 두 직선 사이의 각도 (평행도 지표)
            double bDirLen = Math.Sqrt(b.DirectionX * b.DirectionX + b.DirectionY * b.DirectionY);
            double parallelAngle = 0;
            if (bDirLen > 1e-12)
            {
                double dot = Math.Abs(dx * b.DirectionX + dy * b.DirectionY) / bDirLen;
                dot = Math.Clamp(dot, -1.0, 1.0);
                parallelAngle = Math.Acos(dot) * 180.0 / Math.PI;
            }

            result.Data["Distance"] = distance;
            result.Data["SignedDistance"] = cross;
            result.Data["FootAX"] = footAX;
            result.Data["FootAY"] = footAY;
            result.Data["ParallelAngle"] = parallelAngle;

            // 시각화: 수선 (B 기준점 → A 직선 위 수선의 발)
            Cv2.Line(overlay,
                new Point((int)b.X, (int)b.Y),
                new Point((int)footAX, (int)footAY),
                new Scalar(0, 255, 0), 2);
            Cv2.Circle(overlay, new Point((int)footAX, (int)footAY), 4, new Scalar(0, 255, 0), -1);
            Cv2.Circle(overlay, new Point((int)b.X, (int)b.Y), 4, new Scalar(0, 255, 0), -1);

            var mid = new Point((int)((b.X + footAX) / 2) + 5, (int)((b.Y + footAY) / 2) - 5);
            Cv2.PutText(overlay, $"{distance:F2}px", mid,
                HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 255), 1);

            result.Success = true;
            result.Message = parallelAngle < 1.0
                ? $"직선 간 거리: {distance:F2}px (평행)"
                : $"직선 간 거리: {distance:F2}px (기울기 차: {parallelAngle:F2}°)";
        }

        private void ComputeLineLineAngle(SourceGeometry a, SourceGeometry b,
            VisionResult result, Mat overlay)
        {
            if (a.Type != GeometryType.Line || b.Type != GeometryType.Line)
            {
                result.Success = false;
                result.Message = "LineLineAngle: 두 Line 요소가 필요합니다.";
                return;
            }

            double ax = a.DirectionX, ay = a.DirectionY;
            double bx = b.DirectionX, by = b.DirectionY;

            double lenA = Math.Sqrt(ax * ax + ay * ay);
            double lenB = Math.Sqrt(bx * bx + by * by);
            if (lenA < 1e-12 || lenB < 1e-12)
            { result.Success = false; result.Message = "유효하지 않은 직선."; return; }

            double dot = (ax * bx + ay * by) / (lenA * lenB);
            dot = Math.Clamp(dot, -1.0, 1.0);
            double angleRad = Math.Acos(Math.Abs(dot));
            double angleDeg = angleRad * 180.0 / Math.PI;

            // 부호 있는 각도 (외적 기반)
            double cross = ax * by - ay * bx;
            double signedAngleDeg = Math.Atan2(cross, ax * bx + ay * by) * 180.0 / Math.PI;

            result.Data["Angle"] = angleDeg;
            result.Data["SignedAngle"] = signedAngleDeg;
            result.Data["LineAAngle"] = a.Angle;
            result.Data["LineBAngle"] = b.Angle;

            // 시각화: 교차 영역 근처에 각도 표시
            // 교차점이 있으면 거기에, 없으면 이미지 중앙에
            double ix, iy;
            if (TryGetLineIntersection(a, b, out ix, out iy))
            {
                Cv2.PutText(overlay, $"{angleDeg:F2}°",
                    new Point((int)ix + 10, (int)iy - 10),
                    HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
            }

            result.Success = true;
            result.Message = $"각도: {angleDeg:F2}°";
        }

        private void ComputeLineLineIntersection(SourceGeometry a, SourceGeometry b,
            VisionResult result, Mat overlay)
        {
            if (a.Type != GeometryType.Line || b.Type != GeometryType.Line)
            {
                result.Success = false;
                result.Message = "LineLineIntersection: 두 Line 요소가 필요합니다.";
                return;
            }

            if (!TryGetLineIntersection(a, b, out double ix, out double iy))
            {
                result.Success = false;
                result.Message = "두 직선이 평행하여 교차점이 없습니다.";
                return;
            }

            result.Data["IntersectionX"] = ix;
            result.Data["IntersectionY"] = iy;
            result.Data["CenterX"] = ix;
            result.Data["CenterY"] = iy;

            // 시각화
            Cv2.Circle(overlay, new Point((int)ix, (int)iy), 6, new Scalar(0, 255, 0), -1);
            Cv2.DrawMarker(overlay, new Point((int)ix, (int)iy),
                new Scalar(0, 255, 0), MarkerTypes.Cross, 20, 2);
            Cv2.PutText(overlay, $"({ix:F1}, {iy:F1})",
                new Point((int)ix + 10, (int)iy - 10),
                HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 255), 1);

            result.Success = true;
            result.Message = $"교차점: ({ix:F1}, {iy:F1})";
        }

        private void ComputeLineCircleIntersection(SourceGeometry a, SourceGeometry b,
            VisionResult result, Mat overlay)
        {
            SourceGeometry? lineSrc, circleSrc;
            if (a.Type == GeometryType.Line && b.Type == GeometryType.Circle)
            { lineSrc = a; circleSrc = b; }
            else if (a.Type == GeometryType.Circle && b.Type == GeometryType.Line)
            { lineSrc = b; circleSrc = a; }
            else
            {
                result.Success = false;
                result.Message = "LineCircleIntersection: Line 1개와 Circle 1개가 필요합니다.";
                return;
            }

            // 직선: P + t*D, 원: center(cx,cy), radius r
            double px = lineSrc.X, py = lineSrc.Y;
            double dx = lineSrc.DirectionX, dy = lineSrc.DirectionY;
            double dirLen = Math.Sqrt(dx * dx + dy * dy);
            if (dirLen < 1e-12) { result.Success = false; result.Message = "유효하지 않은 직선."; return; }
            dx /= dirLen; dy /= dirLen;

            double cx = circleSrc.X, cy = circleSrc.Y, r = circleSrc.Radius;

            // |P + tD - C|² = r²
            double ocx = px - cx, ocy = py - cy;
            double a2 = 1.0; // dx²+dy² = 1 (정규화됨)
            double b2 = 2.0 * (ocx * dx + ocy * dy);
            double c2 = ocx * ocx + ocy * ocy - r * r;
            double discriminant = b2 * b2 - 4.0 * a2 * c2;

            if (discriminant < 0)
            {
                result.Success = false;
                result.Message = "직선과 원이 교차하지 않습니다.";
                return;
            }

            double sqrtD = Math.Sqrt(discriminant);
            double t1 = (-b2 - sqrtD) / (2.0 * a2);
            double t2 = (-b2 + sqrtD) / (2.0 * a2);

            double ix1 = px + t1 * dx, iy1 = py + t1 * dy;
            double ix2 = px + t2 * dx, iy2 = py + t2 * dy;

            int intersectionCount = discriminant < 1e-6 ? 1 : 2;
            result.Data["IntersectionCount"] = intersectionCount;
            result.Data["Intersection1X"] = ix1;
            result.Data["Intersection1Y"] = iy1;
            if (intersectionCount == 2)
            {
                result.Data["Intersection2X"] = ix2;
                result.Data["Intersection2Y"] = iy2;
            }

            // 시각화
            Cv2.Circle(overlay, new Point((int)ix1, (int)iy1), 5, new Scalar(0, 255, 0), -1);
            if (intersectionCount == 2)
                Cv2.Circle(overlay, new Point((int)ix2, (int)iy2), 5, new Scalar(0, 255, 0), -1);

            result.Success = true;
            result.Message = intersectionCount == 2
                ? $"교차점 2개: ({ix1:F1},{iy1:F1}), ({ix2:F1},{iy2:F1})"
                : $"접점 1개: ({ix1:F1},{iy1:F1})";
        }

        private void ComputeCircleCircleDistance(SourceGeometry a, SourceGeometry b,
            VisionResult result, Mat overlay)
        {
            if (a.Type != GeometryType.Circle || b.Type != GeometryType.Circle)
            {
                result.Success = false;
                result.Message = "CircleCircleDistance: 두 Circle 요소가 필요합니다.";
                return;
            }

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double centerDistance = Math.Sqrt(dx * dx + dy * dy);
            double edgeDistance = centerDistance - a.Radius - b.Radius;

            result.Data["CenterDistance"] = centerDistance;
            result.Data["EdgeDistance"] = edgeDistance;
            result.Data["CenterAX"] = a.X;
            result.Data["CenterAY"] = a.Y;
            result.Data["RadiusA"] = a.Radius;
            result.Data["CenterBX"] = b.X;
            result.Data["CenterBY"] = b.Y;
            result.Data["RadiusB"] = b.Radius;

            // 시각화: 중심 간 거리선
            Cv2.Line(overlay,
                new Point((int)a.X, (int)a.Y),
                new Point((int)b.X, (int)b.Y),
                new Scalar(0, 255, 0), 2);

            var mid = new Point((int)((a.X + b.X) / 2) + 5, (int)((a.Y + b.Y) / 2) - 5);
            Cv2.PutText(overlay, $"{centerDistance:F2}px", mid,
                HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 255), 1);

            result.Success = true;
            result.Message = $"중심 거리: {centerDistance:F2}px, 엣지 거리: {edgeDistance:F2}px";
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 기하 요소에서 Point 좌표를 추출 (Point/Circle의 중심/Line의 기준점)
        /// </summary>
        private static Point2d? EnsurePoint(SourceGeometry geo)
        {
            return new Point2d(geo.X, geo.Y);
        }

        /// <summary>
        /// 두 직선의 교차점 계산
        /// </summary>
        private static bool TryGetLineIntersection(SourceGeometry a, SourceGeometry b,
            out double ix, out double iy)
        {
            ix = iy = 0;

            double ax = a.DirectionX, ay = a.DirectionY;
            double bx = b.DirectionX, by = b.DirectionY;

            // 크래머 법칙: A + s*dA = B + t*dB
            double det = ax * (-by) - (-bx) * ay;
            if (Math.Abs(det) < 1e-12)
                return false; // 평행

            double dPx = b.X - a.X;
            double dPy = b.Y - a.Y;

            double s = (dPx * (-by) - (-bx) * dPy) / det;

            ix = a.X + s * ax;
            iy = a.Y + s * ay;
            return true;
        }

        /// <summary>
        /// 소스 기하 요소를 오버레이에 시각화
        /// </summary>
        private static void DrawSourceGeometry(Mat overlay, SourceGeometry geo, Scalar color)
        {
            switch (geo.Type)
            {
                case GeometryType.Point:
                    Cv2.DrawMarker(overlay, new Point((int)geo.X, (int)geo.Y),
                        color, MarkerTypes.Cross, 15, 2);
                    break;

                case GeometryType.Line:
                    // 직선을 이미지 범위 내에서 충분히 길게 그리기
                    double ext = 2000;
                    double dx = geo.DirectionX, dy = geo.DirectionY;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 1e-12)
                    {
                        dx /= len; dy /= len;
                        Cv2.Line(overlay,
                            new Point((int)(geo.X - dx * ext), (int)(geo.Y - dy * ext)),
                            new Point((int)(geo.X + dx * ext), (int)(geo.Y + dy * ext)),
                            color, 1);
                    }
                    break;

                case GeometryType.Circle:
                    Cv2.Circle(overlay, new Point((int)geo.X, (int)geo.Y),
                        (int)geo.Radius, color, 1);
                    Cv2.DrawMarker(overlay, new Point((int)geo.X, (int)geo.Y),
                        color, MarkerTypes.Cross, 10, 1);
                    break;
            }
        }

        /// <summary>
        /// 상위 도구의 결과 데이터에서 기하 요소를 추출
        /// </summary>
        public static SourceGeometry? ExtractGeometry(string toolId, string toolName,
            string toolType, VisionResult sourceResult)
        {
            if (!sourceResult.Success) return null;

            var data = sourceResult.Data;
            var geo = new SourceGeometry { ToolId = toolId, ToolName = toolName };

            switch (toolType)
            {
                case "LineFitTool":
                    if (data.TryGetValue("LinePointX", out var lpx) &&
                        data.TryGetValue("LinePointY", out var lpy) &&
                        data.TryGetValue("DirectionX", out var ldx) &&
                        data.TryGetValue("DirectionY", out var ldy))
                    {
                        geo.Type = GeometryType.Line;
                        geo.X = Convert.ToDouble(lpx);
                        geo.Y = Convert.ToDouble(lpy);
                        geo.DirectionX = Convert.ToDouble(ldx);
                        geo.DirectionY = Convert.ToDouble(ldy);
                        geo.Angle = data.TryGetValue("LineAngle", out var la) ? Convert.ToDouble(la) : 0;
                        return geo;
                    }
                    break;

                case "CircleFitTool":
                    if (data.TryGetValue("CenterX", out var ccx) &&
                        data.TryGetValue("CenterY", out var ccy) &&
                        data.TryGetValue("Radius", out var cr))
                    {
                        geo.Type = GeometryType.Circle;
                        geo.X = Convert.ToDouble(ccx);
                        geo.Y = Convert.ToDouble(ccy);
                        geo.Radius = Convert.ToDouble(cr);
                        return geo;
                    }
                    break;

                case "CaliperTool":
                    // SingleEdge → Point, EdgePair → 두 점의 중심점
                    if (data.TryGetValue("EdgeX", out var ex) &&
                        data.TryGetValue("EdgeY", out var ey))
                    {
                        geo.Type = GeometryType.Point;
                        geo.X = Convert.ToDouble(ex);
                        geo.Y = Convert.ToDouble(ey);
                        return geo;
                    }
                    else if (data.TryGetValue("CenterX", out var cpx) &&
                             data.TryGetValue("CenterY", out var cpy))
                    {
                        geo.Type = GeometryType.Point;
                        geo.X = Convert.ToDouble(cpx);
                        geo.Y = Convert.ToDouble(cpy);
                        return geo;
                    }
                    break;

                case "FeatureMatchTool":
                    if (data.TryGetValue("CenterX", out var fmx) &&
                        data.TryGetValue("CenterY", out var fmy))
                    {
                        geo.Type = GeometryType.Point;
                        geo.X = Convert.ToDouble(fmx);
                        geo.Y = Convert.ToDouble(fmy);
                        return geo;
                    }
                    break;

                case "BlobTool":
                    if (data.TryGetValue("CenterX", out var bx) &&
                        data.TryGetValue("CenterY", out var by))
                    {
                        geo.Type = GeometryType.Point;
                        geo.X = Convert.ToDouble(bx);
                        geo.Y = Convert.ToDouble(by);
                        return geo;
                    }
                    break;

                case "GeometryTool":
                    // 다른 GeometryTool의 결과도 활용 가능
                    if (data.TryGetValue("IntersectionX", out var gix) &&
                        data.TryGetValue("IntersectionY", out var giy))
                    {
                        geo.Type = GeometryType.Point;
                        geo.X = Convert.ToDouble(gix);
                        geo.Y = Convert.ToDouble(giy);
                        return geo;
                    }
                    break;
            }

            return null;
        }

        #endregion

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Distance", "DeltaX", "DeltaY",
                "Angle", "SignedAngle",
                "IntersectionX", "IntersectionY",
                "FootX", "FootY", "SignedDistance",
                "FootAX", "FootAY", "ParallelAngle",
                "CenterDistance", "EdgeDistance",
                "IntersectionCount", "Intersection1X", "Intersection1Y",
                "Intersection2X", "Intersection2Y"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new GeometryTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                Operation = this.Operation
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
