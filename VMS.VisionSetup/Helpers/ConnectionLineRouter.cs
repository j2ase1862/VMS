using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Helpers
{
    /// <summary>
    /// 연결선을 베지어 곡선으로 라우팅하는 유틸리티 클래스.
    /// 장애물(다른 도구 컨트롤)을 자동으로 우회하는 부드러운 경로를 계산합니다.
    /// </summary>
    public static class ConnectionLineRouter
    {
        // 도구 Border의 예상 크기 (XAML 템플릿 기준: MinWidth=150, 헤더+바디 ≈ 50)
        private const double ToolWidth = 150;
        private const double ToolHeight = 50;
        private const double ObstaclePadding = 15;

        /// <summary>
        /// 연결선 경로 계산 결과
        /// </summary>
        public class ConnectionPathResult
        {
            public PathGeometry PathGeometry { get; set; } = new();
            public Point ArrowTipPoint { get; set; }
            public double ArrowAngle { get; set; }
            public Point LabelPosition { get; set; }
        }

        /// <summary>
        /// 완성된 연결에 대한 베지어 경로 계산 (장애물 우회 포함)
        /// </summary>
        public static ConnectionPathResult ComputePath(
            ToolItem source,
            ToolItem target,
            IEnumerable<ToolItem> allTools)
        {
            var sourceRect = GetToolRect(source);
            var targetRect = GetToolRect(target);
            var sourceCenter = GetRectCenter(sourceRect);
            var targetCenter = GetRectCenter(targetRect);

            // 가장자리 앵커 포인트 계산 (도구 테두리에서 출발/도착)
            var sourceAnchor = GetEdgeAnchor(sourceRect, targetCenter);
            var targetAnchor = GetEdgeAnchor(targetRect, sourceCenter);

            // 장애물 탐색 (소스/타겟 제외)
            var obstacles = FindObstacles(sourceAnchor, targetAnchor, source, target, allTools);

            PathGeometry pathGeometry;
            Point arrowTip;
            double arrowAngle;
            Point labelPos;

            if (obstacles.Count == 0)
            {
                // 장애물 없음: 단순 베지어 곡선
                pathGeometry = CreateSimpleBezier(sourceAnchor, targetAnchor, sourceRect, targetRect);
            }
            else
            {
                // 장애물 있음: 웨이포인트를 통한 라우팅
                var waypoints = ComputeRoutingWaypoints(sourceAnchor, targetAnchor, obstacles);
                pathGeometry = CreateSmoothedPath(sourceAnchor, waypoints, targetAnchor);
            }

            // 화살표 끝점 및 각도 계산 (곡선의 끝 접선 방향)
            ComputeArrowInfo(pathGeometry, out arrowTip, out arrowAngle);

            // 라벨 위치 (경로 중간점)
            pathGeometry.GetPointAtFractionLength(0.5, out Point midPoint, out _);
            labelPos = midPoint;

            return new ConnectionPathResult
            {
                PathGeometry = pathGeometry,
                ArrowTipPoint = arrowTip,
                ArrowAngle = arrowAngle,
                LabelPosition = labelPos
            };
        }

        /// <summary>
        /// 연결 모드에서 마우스 추적용 임시 베지어 경로 계산
        /// </summary>
        public static PathGeometry ComputeTempPath(Point sourceCenter, Point mousePos)
        {
            double dx = mousePos.X - sourceCenter.X;
            double dy = mousePos.Y - sourceCenter.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double offset = Math.Min(dist * 0.3, 80);

            // 소스→타겟 방향의 주요 축에 따라 컨트롤 포인트 배치
            Point cp1, cp2;
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                // 수평 우세: 수평으로 벌림
                cp1 = new Point(sourceCenter.X + Math.Sign(dx) * offset, sourceCenter.Y);
                cp2 = new Point(mousePos.X - Math.Sign(dx) * offset, mousePos.Y);
            }
            else
            {
                // 수직 우세: 수직으로 벌림
                cp1 = new Point(sourceCenter.X, sourceCenter.Y + Math.Sign(dy) * offset);
                cp2 = new Point(mousePos.X, mousePos.Y - Math.Sign(dy) * offset);
            }

            var figure = new PathFigure { StartPoint = sourceCenter, IsClosed = false };
            figure.Segments.Add(new BezierSegment(cp1, cp2, mousePos, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        #region Private Helpers

        /// <summary>
        /// 도구의 바운딩 사각형 계산
        /// </summary>
        private static Rect GetToolRect(ToolItem tool)
        {
            return new Rect(tool.X, tool.Y, ToolWidth, ToolHeight);
        }

        /// <summary>
        /// 사각형의 중심점 반환
        /// </summary>
        private static Point GetRectCenter(Rect rect)
        {
            return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }

        /// <summary>
        /// 도구 사각형 가장자리에서 대상 방향으로의 앵커 포인트 계산.
        /// 대상에 가장 가까운 변(상/하/좌/우)의 중앙에서 출발합니다.
        /// </summary>
        private static Point GetEdgeAnchor(Rect rect, Point targetPoint)
        {
            var center = GetRectCenter(rect);
            double dx = targetPoint.X - center.X;
            double dy = targetPoint.Y - center.Y;

            // 가로/세로 비율로 어느 변에서 나갈지 결정
            double halfW = rect.Width / 2;
            double halfH = rect.Height / 2;

            // 비율 비교 (dx/halfW vs dy/halfH)
            double absDx = Math.Abs(dx);
            double absDy = Math.Abs(dy);

            if (absDx * halfH >= absDy * halfW)
            {
                // 좌우 변
                if (dx >= 0)
                    return new Point(rect.Right, center.Y); // 우측
                else
                    return new Point(rect.Left, center.Y); // 좌측
            }
            else
            {
                // 상하 변
                if (dy >= 0)
                    return new Point(center.X, rect.Bottom); // 하단
                else
                    return new Point(center.X, rect.Top); // 상단
            }
        }

        /// <summary>
        /// 앵커 포인트가 사각형의 어느 변에 있는지 반환 (컨트롤 포인트 방향 결정용)
        /// </summary>
        private static EdgeSide GetEdgeSide(Rect rect, Point anchor)
        {
            const double epsilon = 1.0;
            if (Math.Abs(anchor.X - rect.Right) < epsilon) return EdgeSide.Right;
            if (Math.Abs(anchor.X - rect.Left) < epsilon) return EdgeSide.Left;
            if (Math.Abs(anchor.Y - rect.Bottom) < epsilon) return EdgeSide.Bottom;
            return EdgeSide.Top;
        }

        private enum EdgeSide { Top, Bottom, Left, Right }

        /// <summary>
        /// 소스→타겟 직선 경로상의 장애물 도구 탐색
        /// </summary>
        private static List<Rect> FindObstacles(
            Point source, Point target,
            ToolItem sourceTool, ToolItem targetTool,
            IEnumerable<ToolItem> allTools)
        {
            var obstacles = new List<Rect>();

            foreach (var tool in allTools)
            {
                if (tool.Id == sourceTool.Id || tool.Id == targetTool.Id)
                    continue;

                var rect = GetToolRect(tool);
                var inflated = new Rect(
                    rect.X - ObstaclePadding,
                    rect.Y - ObstaclePadding,
                    rect.Width + ObstaclePadding * 2,
                    rect.Height + ObstaclePadding * 2);

                if (LineIntersectsRect(source, target, inflated))
                {
                    obstacles.Add(inflated);
                }
            }

            // 소스로부터 거리순 정렬
            obstacles.Sort((a, b) =>
            {
                double distA = Distance(source, GetRectCenter(a));
                double distB = Distance(source, GetRectCenter(b));
                return distA.CompareTo(distB);
            });

            return obstacles;
        }

        /// <summary>
        /// 선분과 사각형의 교차 검사 (Liang-Barsky 알고리즘)
        /// </summary>
        private static bool LineIntersectsRect(Point p1, Point p2, Rect rect)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;

            double[] p = { -dx, dx, -dy, dy };
            double[] q = { p1.X - rect.Left, rect.Right - p1.X, p1.Y - rect.Top, rect.Bottom - p1.Y };

            double tMin = 0;
            double tMax = 1;

            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-10)
                {
                    if (q[i] < 0) return false;
                }
                else
                {
                    double t = q[i] / p[i];
                    if (p[i] < 0)
                    {
                        if (t > tMax) return false;
                        if (t > tMin) tMin = t;
                    }
                    else
                    {
                        if (t < tMin) return false;
                        if (t < tMax) tMax = t;
                    }
                }
            }

            return tMin <= tMax;
        }

        /// <summary>
        /// 장애물을 우회하는 웨이포인트 계산.
        /// 각 장애물에 대해 가장 적은 편차의 우회 방향을 선택합니다.
        /// </summary>
        private static List<Point> ComputeRoutingWaypoints(
            Point source, Point target, List<Rect> obstacles)
        {
            var waypoints = new List<Point>();

            // 소스→타겟의 이상적 직선 방향
            double idealDx = target.X - source.X;
            double idealDy = target.Y - source.Y;

            foreach (var obs in obstacles)
            {
                var center = GetRectCenter(obs);

                // 우회 후보 4개 (사각형의 각 변 중간 + 패딩)
                var candidates = new[]
                {
                    new Point(center.X, obs.Top - ObstaclePadding),    // 상단
                    new Point(center.X, obs.Bottom + ObstaclePadding), // 하단
                    new Point(obs.Left - ObstaclePadding, center.Y),   // 좌측
                    new Point(obs.Right + ObstaclePadding, center.Y)   // 우측
                };

                // 이상적 직선에서 가장 적게 벗어나는 후보 선택
                Point best = candidates[0];
                double bestScore = double.MaxValue;

                foreach (var candidate in candidates)
                {
                    // 직선으로부터의 수직 거리 + 총 경로 길이 증가량으로 스코어링
                    double detour = Distance(source, candidate) + Distance(candidate, target);
                    double directDist = Distance(source, target);
                    double penalty = detour - directDist;

                    // 직선의 방향과 같은 쪽에 있는 후보에 보너스
                    double candidateDx = candidate.X - source.X;
                    double candidateDy = candidate.Y - source.Y;
                    double alignment = (candidateDx * idealDx + candidateDy * idealDy)
                                       / (Distance(source, target) + 1e-10);

                    double score = penalty - alignment * 0.3;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                waypoints.Add(best);
            }

            return waypoints;
        }

        /// <summary>
        /// 장애물 없는 경우: 단순 3차 베지어 곡선 생성.
        /// 컨트롤 포인트는 앵커 포인트의 변 방향으로 밀어냅니다.
        /// </summary>
        private static PathGeometry CreateSimpleBezier(
            Point sourceAnchor, Point targetAnchor,
            Rect sourceRect, Rect targetRect)
        {
            double dist = Distance(sourceAnchor, targetAnchor);
            double offset = Math.Max(30, Math.Min(dist * 0.35, 120));

            var sourceSide = GetEdgeSide(sourceRect, sourceAnchor);
            var targetSide = GetEdgeSide(targetRect, targetAnchor);

            var cp1 = PushControlPoint(sourceAnchor, sourceSide, offset);
            var cp2 = PushControlPoint(targetAnchor, targetSide, offset);

            var figure = new PathFigure { StartPoint = sourceAnchor, IsClosed = false };
            figure.Segments.Add(new BezierSegment(cp1, cp2, targetAnchor, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        /// <summary>
        /// 앵커 변 방향으로 컨트롤 포인트를 밀어냄
        /// </summary>
        private static Point PushControlPoint(Point anchor, EdgeSide side, double offset)
        {
            return side switch
            {
                EdgeSide.Right => new Point(anchor.X + offset, anchor.Y),
                EdgeSide.Left => new Point(anchor.X - offset, anchor.Y),
                EdgeSide.Bottom => new Point(anchor.X, anchor.Y + offset),
                EdgeSide.Top => new Point(anchor.X, anchor.Y - offset),
                _ => anchor
            };
        }

        /// <summary>
        /// 웨이포인트를 통과하는 부드러운 Catmull-Rom → Bezier 스플라인 생성.
        /// C1 연속성을 보장합니다.
        /// </summary>
        private static PathGeometry CreateSmoothedPath(
            Point source, List<Point> waypoints, Point target)
        {
            // 전체 포인트 시퀀스: source, waypoints..., target
            var points = new List<Point> { source };
            points.AddRange(waypoints);
            points.Add(target);

            var figure = new PathFigure { StartPoint = source, IsClosed = false };

            if (points.Count == 2)
            {
                // 포인트가 2개뿐이면 직선
                figure.Segments.Add(new LineSegment(target, true));
            }
            else
            {
                // Catmull-Rom 스플라인을 Bezier 세그먼트로 변환
                for (int i = 0; i < points.Count - 1; i++)
                {
                    // Catmull-Rom에 필요한 4개 포인트 (P0, P1, P2, P3)
                    var p0 = (i == 0) ? points[i] : points[i - 1];
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    var p3 = (i + 2 < points.Count) ? points[i + 2] : points[i + 1];

                    // Catmull-Rom → Cubic Bezier 변환
                    // CP1 = P1 + (P2 - P0) / 6
                    // CP2 = P2 - (P3 - P1) / 6
                    var cp1 = new Point(
                        p1.X + (p2.X - p0.X) / 6.0,
                        p1.Y + (p2.Y - p0.Y) / 6.0);
                    var cp2 = new Point(
                        p2.X - (p3.X - p1.X) / 6.0,
                        p2.Y - (p3.Y - p1.Y) / 6.0);

                    figure.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
                }
            }

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        /// <summary>
        /// 경로 끝점에서 화살표 방향 정보 계산
        /// </summary>
        private static void ComputeArrowInfo(PathGeometry pathGeometry, out Point tipPoint, out double angle)
        {
            // 끝점(t=1.0)에서의 위치와 접선 벡터
            pathGeometry.GetPointAtFractionLength(1.0, out tipPoint, out Point tangent);
            angle = Math.Atan2(tangent.Y, tangent.X);
        }

        /// <summary>
        /// 두 점 사이의 유클리드 거리
        /// </summary>
        private static double Distance(Point a, Point b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        #endregion
    }
}
