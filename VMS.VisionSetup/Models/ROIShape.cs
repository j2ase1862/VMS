using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using CvRect = OpenCvSharp.Rect;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using WpfPoint = System.Windows.Point;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// ROI 형태 타입
    /// </summary>
    public enum ROIShapeType
    {
        Rectangle,
        RectangleAffine,  // 회전 가능한 사각형
        Circle,
        Ellipse,
        Polygon,
        Line
    }

    /// <summary>
    /// ROI 핸들 타입 (리사이즈/회전용)
    /// </summary>
    public enum HandleType
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left,
        Center,       // 이동용
        Rotate        // 회전용
    }

    /// <summary>
    /// ROI 도형 기본 클래스
    /// </summary>
    public abstract partial class ROIShape : ObservableObject
    {
        [ObservableProperty]
        private string _name = "ROI";

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isVisible = true;

        [ObservableProperty]
        private System.Windows.Media.Color _color = System.Windows.Media.Colors.Lime;

        [ObservableProperty]
        private double _strokeThickness = 2;

        public abstract ROIShapeType ShapeType { get; }

        /// <summary>
        /// OpenCV Rect로 변환 (바운딩 박스)
        /// </summary>
        public abstract CvRect GetBoundingRect();

        /// <summary>
        /// OpenCV 마스크 이미지 생성
        /// </summary>
        public abstract Mat CreateMask(int width, int height);

        /// <summary>
        /// 점이 ROI 내부에 있는지 확인
        /// </summary>
        public abstract bool ContainsPoint(System.Windows.Point point);

        /// <summary>
        /// 핸들 위치 반환
        /// </summary>
        public abstract Dictionary<HandleType, System.Windows.Point> GetHandles();

        /// <summary>
        /// 이동
        /// </summary>
        public abstract void Move(double deltaX, double deltaY);

        /// <summary>
        /// 핸들로 크기/형태 조정
        /// </summary>
        public abstract void ResizeByHandle(HandleType handle, System.Windows.Point newPosition);

        /// <summary>
        /// 복제
        /// </summary>
        public abstract ROIShape Clone();
    }

    /// <summary>
    /// 사각형 ROI
    /// </summary>
    public partial class RectangleROI : ROIShape
    {
        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;

        [ObservableProperty]
        private double _width = 100;

        [ObservableProperty]
        private double _height = 100;

        public override ROIShapeType ShapeType => ROIShapeType.Rectangle;

        public RectangleROI() { }

        public RectangleROI(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public override CvRect GetBoundingRect()
        {
            // 음수 너비/높이(역방향 드래그)를 고려하여 실제 좌상단 좌표와 양수 크기 계산
            int realX = (int)Math.Min(X, X + Width);
            int realY = (int)Math.Min(Y, Y + Height);
            int realWidth = (int)Math.Abs(Width);
            int realHeight = (int)Math.Abs(Height);

            return new CvRect(realX, realY, realWidth, realHeight);
        }

        public override Mat CreateMask(int width, int height)
        {
            var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(mask, GetBoundingRect(), Scalar.White, -1);
            return mask;
        }

        public override bool ContainsPoint(System.Windows.Point point)
        {
            return point.X >= X && point.X <= X + Width &&
                   point.Y >= Y && point.Y <= Y + Height;
        }

        public override Dictionary<HandleType, System.Windows.Point> GetHandles()
        {
            return new Dictionary<HandleType, System.Windows.Point>
            {
                { HandleType.TopLeft, new System.Windows.Point(X, Y) },
                { HandleType.Top, new System.Windows.Point(X + Width / 2, Y) },
                { HandleType.TopRight, new System.Windows.Point(X + Width, Y) },
                { HandleType.Right, new System.Windows.Point(X + Width, Y + Height / 2) },
                { HandleType.BottomRight, new System.Windows.Point(X + Width, Y + Height) },
                { HandleType.Bottom, new System.Windows.Point(X + Width / 2, Y + Height) },
                { HandleType.BottomLeft, new System.Windows.Point(X, Y + Height) },
                { HandleType.Left, new System.Windows.Point(X, Y + Height / 2) },
                { HandleType.Center, new System.Windows.Point(X + Width / 2, Y + Height / 2) }
            };
        }

        public override void Move(double deltaX, double deltaY)
        {
            X += deltaX;
            Y += deltaY;
        }

        public override void ResizeByHandle(HandleType handle, System.Windows.Point newPosition)
        {
            switch (handle)
            {
                case HandleType.TopLeft:
                    Width += X - newPosition.X;
                    Height += Y - newPosition.Y;
                    X = newPosition.X;
                    Y = newPosition.Y;
                    break;
                case HandleType.Top:
                    Height += Y - newPosition.Y;
                    Y = newPosition.Y;
                    break;
                case HandleType.TopRight:
                    Width = newPosition.X - X;
                    Height += Y - newPosition.Y;
                    Y = newPosition.Y;
                    break;
                case HandleType.Right:
                    Width = newPosition.X - X;
                    break;
                case HandleType.BottomRight:
                    Width = newPosition.X - X;
                    Height = newPosition.Y - Y;
                    break;
                case HandleType.Bottom:
                    Height = newPosition.Y - Y;
                    break;
                case HandleType.BottomLeft:
                    Width += X - newPosition.X;
                    X = newPosition.X;
                    Height = newPosition.Y - Y;
                    break;
                case HandleType.Left:
                    Width += X - newPosition.X;
                    X = newPosition.X;
                    break;
            }

            // 최소 크기 보장
            if (Width < 10) Width = 10;
            if (Height < 10) Height = 10;
        }

        public override ROIShape Clone()
        {
            return new RectangleROI(X, Y, Width, Height)
            {
                Name = this.Name,
                Color = this.Color,
                StrokeThickness = this.StrokeThickness
            };
        }
    }

    /// <summary>
    /// 회전 가능한 사각형 ROI (Affine)
    /// </summary>
    public partial class RectangleAffineROI : ROIShape
    {
        [ObservableProperty]
        private double _centerX;

        [ObservableProperty]
        private double _centerY;

        [ObservableProperty]
        private double _width = 100;

        [ObservableProperty]
        private double _height = 100;

        [ObservableProperty]
        private double _angle;  // 회전 각도 (도)

        public override ROIShapeType ShapeType => ROIShapeType.RectangleAffine;

        public RectangleAffineROI() { }

        public RectangleAffineROI(double centerX, double centerY, double width, double height, double angle = 0)
        {
            CenterX = centerX;
            CenterY = centerY;
            Width = width;
            Height = height;
            Angle = angle;
        }

        /// <summary>
        /// 회전된 사각형의 4개 꼭짓점 반환
        /// </summary>
        public System.Windows.Point[] GetCorners()
        {
            double radians = Angle * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            double halfW = Width / 2;
            double halfH = Height / 2;

            // 회전 전 상대 좌표
            var corners = new[]
            {
                new System.Windows.Point(-halfW, -halfH),
                new System.Windows.Point(halfW, -halfH),
                new System.Windows.Point(halfW, halfH),
                new System.Windows.Point(-halfW, halfH)
            };

            // 회전 및 이동 적용
            for (int i = 0; i < 4; i++)
            {
                double x = corners[i].X * cos - corners[i].Y * sin + CenterX;
                double y = corners[i].X * sin + corners[i].Y * cos + CenterY;
                corners[i] = new System.Windows.Point(x, y);
            }

            return corners;
        }

        public override CvRect GetBoundingRect()
        {
            // CenterX/Y를 사용하는 회전 사각형은 GetCorners()를 활용해 전체 영역(Bounding Box) 계산
            var corners = GetCorners();
            if (corners == null || corners.Length == 0) return new CvRect();

            double minX = corners.Min(p => p.X);
            double minY = corners.Min(p => p.Y);
            double maxX = corners.Max(p => p.X);
            double maxY = corners.Max(p => p.Y);

            return new CvRect((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
        }

        public override Mat CreateMask(int width, int height)
        {
            var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
            var corners = GetCorners();
            var points = new Point[]
            {
                new Point((int)corners[0].X, (int)corners[0].Y),
                new Point((int)corners[1].X, (int)corners[1].Y),
                new Point((int)corners[2].X, (int)corners[2].Y),
                new Point((int)corners[3].X, (int)corners[3].Y)
            };
            Cv2.FillConvexPoly(mask, points, Scalar.White);
            return mask;
        }

        public override bool ContainsPoint(System.Windows.Point point)
        {
            // 점을 역회전하여 축 정렬된 사각형으로 확인
            double radians = -Angle * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            double dx = point.X - CenterX;
            double dy = point.Y - CenterY;

            double localX = dx * cos - dy * sin;
            double localY = dx * sin + dy * cos;

            return Math.Abs(localX) <= Width / 2 && Math.Abs(localY) <= Height / 2;
        }

        public override Dictionary<HandleType, System.Windows.Point> GetHandles()
        {
            var corners = GetCorners();
            var handles = new Dictionary<HandleType, System.Windows.Point>
            {
                { HandleType.TopLeft, corners[0] },
                { HandleType.TopRight, corners[1] },
                { HandleType.BottomRight, corners[2] },
                { HandleType.BottomLeft, corners[3] },
                { HandleType.Center, new System.Windows.Point(CenterX, CenterY) }
            };

            // 회전 핸들 (위쪽 중앙에서 약간 위에)
            double radians = Angle * Math.PI / 180.0;
            double rotateHandleDistance = Height / 2 + 30;
            handles[HandleType.Rotate] = new System.Windows.Point(
                CenterX - rotateHandleDistance * Math.Sin(radians),
                CenterY - rotateHandleDistance * Math.Cos(radians));

            return handles;
        }

        public override void Move(double deltaX, double deltaY)
        {
            CenterX += deltaX;
            CenterY += deltaY;
        }

        public override void ResizeByHandle(HandleType handle, System.Windows.Point newPosition)
        {
            if (handle == HandleType.Rotate)
            {
                // 회전 각도 계산
                double dx = newPosition.X - CenterX;
                double dy = newPosition.Y - CenterY;
                Angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            }
            else if (handle == HandleType.Center)
            {
                CenterX = newPosition.X;
                CenterY = newPosition.Y;
            }
            else
            {
                // 크기 조정 (회전 고려)
                double radians = -Angle * Math.PI / 180.0;
                double cos = Math.Cos(radians);
                double sin = Math.Sin(radians);

                double dx = newPosition.X - CenterX;
                double dy = newPosition.Y - CenterY;

                double localX = dx * cos - dy * sin;
                double localY = dx * sin + dy * cos;

                switch (handle)
                {
                    case HandleType.TopLeft:
                    case HandleType.BottomRight:
                        Width = Math.Abs(localX) * 2;
                        Height = Math.Abs(localY) * 2;
                        break;
                    case HandleType.TopRight:
                    case HandleType.BottomLeft:
                        Width = Math.Abs(localX) * 2;
                        Height = Math.Abs(localY) * 2;
                        break;
                }
            }

            if (Width < 10) Width = 10;
            if (Height < 10) Height = 10;
        }

        public override ROIShape Clone()
        {
            return new RectangleAffineROI(CenterX, CenterY, Width, Height, Angle)
            {
                Name = this.Name,
                Color = this.Color,
                StrokeThickness = this.StrokeThickness
            };
        }
    }

    /// <summary>
    /// 원형 ROI
    /// </summary>
    public partial class CircleROI : ROIShape
    {
        [ObservableProperty]
        private double _centerX;

        [ObservableProperty]
        private double _centerY;

        [ObservableProperty]
        private double _radius = 50;

        public override ROIShapeType ShapeType => ROIShapeType.Circle;

        public CircleROI() { }

        public CircleROI(double centerX, double centerY, double radius)
        {
            CenterX = centerX;
            CenterY = centerY;
            Radius = radius;
        }

        public override CvRect GetBoundingRect()
        {
            return new CvRect(
                (int)(CenterX - Radius),
                (int)(CenterY - Radius),
                (int)(Radius * 2),
                (int)(Radius * 2));
        }

        public override Mat CreateMask(int width, int height)
        {
            var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
            Cv2.Circle(mask, new Point((int)CenterX, (int)CenterY), (int)Radius, Scalar.White, -1);
            return mask;
        }

        public override bool ContainsPoint(System.Windows.Point point)
        {
            double dx = point.X - CenterX;
            double dy = point.Y - CenterY;
            return Math.Sqrt(dx * dx + dy * dy) <= Radius;
        }

        public override Dictionary<HandleType, System.Windows.Point> GetHandles()
        {
            return new Dictionary<HandleType, System.Windows.Point>
            {
                { HandleType.Center, new System.Windows.Point(CenterX, CenterY) },
                { HandleType.Top, new System.Windows.Point(CenterX, CenterY - Radius) },
                { HandleType.Right, new System.Windows.Point(CenterX + Radius, CenterY) },
                { HandleType.Bottom, new System.Windows.Point(CenterX, CenterY + Radius) },
                { HandleType.Left, new System.Windows.Point(CenterX - Radius, CenterY) }
            };
        }

        public override void Move(double deltaX, double deltaY)
        {
            CenterX += deltaX;
            CenterY += deltaY;
        }

        public override void ResizeByHandle(HandleType handle, System.Windows.Point newPosition)
        {
            if (handle == HandleType.Center)
            {
                CenterX = newPosition.X;
                CenterY = newPosition.Y;
            }
            else
            {
                double dx = newPosition.X - CenterX;
                double dy = newPosition.Y - CenterY;
                Radius = Math.Max(10, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        public override ROIShape Clone()
        {
            return new CircleROI(CenterX, CenterY, Radius)
            {
                Name = this.Name,
                Color = this.Color,
                StrokeThickness = this.StrokeThickness
            };
        }
    }

    /// <summary>
    /// 타원 ROI
    /// </summary>
    public partial class EllipseROI : ROIShape
    {
        [ObservableProperty]
        private double _centerX;

        [ObservableProperty]
        private double _centerY;

        [ObservableProperty]
        private double _radiusX = 80;

        [ObservableProperty]
        private double _radiusY = 50;

        [ObservableProperty]
        private double _angle;

        public override ROIShapeType ShapeType => ROIShapeType.Ellipse;

        public EllipseROI() { }

        public EllipseROI(double centerX, double centerY, double radiusX, double radiusY, double angle = 0)
        {
            CenterX = centerX;
            CenterY = centerY;
            RadiusX = radiusX;
            RadiusY = radiusY;
            Angle = angle;
        }

        public override CvRect GetBoundingRect()
        {
            // 회전된 타원의 바운딩 박스 계산
            double radians = Angle * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            double a = RadiusX * cos;
            double b = RadiusY * sin;
            double c = RadiusX * sin;
            double d = RadiusY * cos;

            double halfW = Math.Sqrt(a * a + b * b);
            double halfH = Math.Sqrt(c * c + d * d);

            return new CvRect(
                (int)(CenterX - halfW),
                (int)(CenterY - halfH),
                (int)(halfW * 2),
                (int)(halfH * 2));
        }

        public override Mat CreateMask(int width, int height)
        {
            var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
            Cv2.Ellipse(mask,
                new Point((int)CenterX, (int)CenterY),
                new Size((int)RadiusX, (int)RadiusY),
                Angle, 0, 360, Scalar.White, -1);
            return mask;
        }

        public override bool ContainsPoint(System.Windows.Point point)
        {
            double radians = -Angle * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            double dx = point.X - CenterX;
            double dy = point.Y - CenterY;

            double localX = dx * cos - dy * sin;
            double localY = dx * sin + dy * cos;

            return (localX * localX) / (RadiusX * RadiusX) + (localY * localY) / (RadiusY * RadiusY) <= 1;
        }

        public override Dictionary<HandleType, System.Windows.Point> GetHandles()
        {
            double radians = Angle * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            return new Dictionary<HandleType, System.Windows.Point>
            {
                { HandleType.Center, new System.Windows.Point(CenterX, CenterY) },
                { HandleType.Right, new System.Windows.Point(CenterX + RadiusX * cos, CenterY + RadiusX * sin) },
                { HandleType.Top, new System.Windows.Point(CenterX - RadiusY * sin, CenterY + RadiusY * cos) },
                { HandleType.Rotate, new System.Windows.Point(
                    CenterX - (RadiusY + 30) * sin,
                    CenterY - (RadiusY + 30) * cos) }
            };
        }

        public override void Move(double deltaX, double deltaY)
        {
            CenterX += deltaX;
            CenterY += deltaY;
        }

        public override void ResizeByHandle(HandleType handle, System.Windows.Point newPosition)
        {
            if (handle == HandleType.Center)
            {
                CenterX = newPosition.X;
                CenterY = newPosition.Y;
            }
            else if (handle == HandleType.Rotate)
            {
                double dx = newPosition.X - CenterX;
                double dy = newPosition.Y - CenterY;
                Angle = Math.Atan2(dy, dx) * 180.0 / Math.PI + 90;
            }
            else if (handle == HandleType.Right)
            {
                double radians = -Angle * Math.PI / 180.0;
                double dx = newPosition.X - CenterX;
                double dy = newPosition.Y - CenterY;
                RadiusX = Math.Max(10, Math.Abs(dx * Math.Cos(radians) + dy * Math.Sin(radians)));
            }
            else if (handle == HandleType.Top)
            {
                double radians = -Angle * Math.PI / 180.0;
                double dx = newPosition.X - CenterX;
                double dy = newPosition.Y - CenterY;
                RadiusY = Math.Max(10, Math.Abs(-dx * Math.Sin(radians) + dy * Math.Cos(radians)));
            }
        }

        public override ROIShape Clone()
        {
            return new EllipseROI(CenterX, CenterY, RadiusX, RadiusY, Angle)
            {
                Name = this.Name,
                Color = this.Color,
                StrokeThickness = this.StrokeThickness
            };
        }
    }

    /// <summary>
    /// 다각형 ROI
    /// </summary>
    public partial class PolygonROI : ROIShape
    {
        [ObservableProperty]
        private List<System.Windows.Point> _points = new();

        public override ROIShapeType ShapeType => ROIShapeType.Polygon;

        public PolygonROI() { }

        public PolygonROI(IEnumerable<System.Windows.Point> points)
        {
            Points = new List<System.Windows.Point>(points);
        }

        public override CvRect GetBoundingRect()
        {
            if (Points.Count == 0)
                return new CvRect();

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var pt in Points)
            {
                minX = Math.Min(minX, pt.X);
                minY = Math.Min(minY, pt.Y);
                maxX = Math.Max(maxX, pt.X);
                maxY = Math.Max(maxY, pt.Y);
            }

            return new CvRect((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
        }

        public override Mat CreateMask(int width, int height)
        {
            var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
            if (Points.Count >= 3)
            {
                var cvPoints = Points.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                Cv2.FillPoly(mask, new[] { cvPoints }, Scalar.White);
            }
            return mask;
        }

        public override bool ContainsPoint(System.Windows.Point point)
        {
            if (Points.Count < 3)
                return false;

            var cvPoints = Points.Select(p => new Point2f((float)p.X, (float)p.Y)).ToArray();
            return Cv2.PointPolygonTest(cvPoints, new Point2f((float)point.X, (float)point.Y), false) >= 0;
        }

        public override Dictionary<HandleType, System.Windows.Point> GetHandles()
        {
            var handles = new Dictionary<HandleType, System.Windows.Point>();

            if (Points.Count > 0)
            {
                // 중심점
                double avgX = Points.Average(p => p.X);
                double avgY = Points.Average(p => p.Y);
                handles[HandleType.Center] = new System.Windows.Point(avgX, avgY);
            }

            return handles;
        }

        public override void Move(double deltaX, double deltaY)
        {
            for (int i = 0; i < Points.Count; i++)
            {
                Points[i] = new System.Windows.Point(Points[i].X + deltaX, Points[i].Y + deltaY);
            }
            OnPropertyChanged(nameof(Points));
        }

        public override void ResizeByHandle(HandleType handle, System.Windows.Point newPosition)
        {
            // 다각형은 개별 점 편집으로 처리
        }

        public void MovePoint(int index, System.Windows.Point newPosition)
        {
            if (index >= 0 && index < Points.Count)
            {
                Points[index] = newPosition;
                OnPropertyChanged(nameof(Points));
            }
        }

        public void AddPoint(System.Windows.Point point)
        {
            Points.Add(point);
            OnPropertyChanged(nameof(Points));
        }

        public override ROIShape Clone()
        {
            return new PolygonROI(Points)
            {
                Name = this.Name,
                Color = this.Color,
                StrokeThickness = this.StrokeThickness
            };
        }
    }
}
