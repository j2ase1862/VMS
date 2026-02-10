using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// Cognex VisionPro 도구를 대체하는 OpenCvSharp 기반 비전 도구의 기본 클래스
    /// </summary>
    public abstract class VisionToolBase : ObservableObject
    {
        /// <summary>
        /// 도구 고유 ID (연결선 매칭용)
        /// </summary>
        public string Id { get; } = Guid.NewGuid().ToString();

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _toolType = string.Empty;
        public string ToolType
        {
            get => _toolType;
            set => SetProperty(ref _toolType, value);
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private double _x;
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        private double _y;
        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        // ROI 설정 (Cognex VisionPro의 Region 대체)
        private Rect _roi = new Rect();
        public Rect ROI
        {
            get => _roi;
            set
            {
                if (SetProperty(ref _roi, value))
                {
                    OnPropertyChanged(nameof(ROIX));
                    OnPropertyChanged(nameof(ROIY));
                    OnPropertyChanged(nameof(ROIWidth));
                    OnPropertyChanged(nameof(ROIHeight));
                }
            }
        }

        // ROI 개별 좌표 프록시 (ToolSettings 바인딩용)
        public int ROIX
        {
            get => _roi.X;
            set { ROI = new Rect(value, _roi.Y, _roi.Width, _roi.Height); }
        }

        public int ROIY
        {
            get => _roi.Y;
            set { ROI = new Rect(_roi.X, value, _roi.Width, _roi.Height); }
        }

        public int ROIWidth
        {
            get => _roi.Width;
            set { ROI = new Rect(_roi.X, _roi.Y, value, _roi.Height); }
        }

        public int ROIHeight
        {
            get => _roi.Height;
            set { ROI = new Rect(_roi.X, _roi.Y, _roi.Width, value); }
        }

        // 캔버스에 표시된 ROI Shape 참조 (도구 전환 시 복원용)
        private ROIShape? _associatedROIShape;
        public ROIShape? AssociatedROIShape
        {
            get => _associatedROIShape;
            set => SetProperty(ref _associatedROIShape, value);
        }

        private bool _useROI = false;
        public bool UseROI
        {
            get => _useROI;
            set => SetProperty(ref _useROI, value);
        }

        internal Rect FixtureBaseROI { get; set; }
        internal bool HasFixtureBaseROI { get; set; }

        // 마지막 실행 결과
        private VisionResult? _lastResult;
        public VisionResult? LastResult
        {
            get => _lastResult;
            set => SetProperty(ref _lastResult, value);
        }

        // 실행 시간 (ms)
        private double _executionTime;
        public double ExecutionTime
        {
            get => _executionTime;
            set => SetProperty(ref _executionTime, value);
        }

        private Mat? _cachedGrayscale;

        public void SetCachedGrayscale(Mat gray)
        {
            _cachedGrayscale?.Dispose();
            _cachedGrayscale = gray;
        }

        protected Mat GetOrConvertGrayscale(Mat inputImage)
        {
            if (_cachedGrayscale != null
                && _cachedGrayscale.Rows == inputImage.Rows
                && _cachedGrayscale.Cols == inputImage.Cols)
            {
                return _cachedGrayscale.Clone();
            }
            return inputImage.Channels() > 1
                ? inputImage.CvtColor(ColorConversionCodes.BGR2GRAY)
                : inputImage.Clone();
        }

        public void ClearCachedGrayscale()
        {
            _cachedGrayscale?.Dispose();
            _cachedGrayscale = null;
        }

        /// <summary>
        /// 도구 실행 - 파생 클래스에서 구현
        /// </summary>
        public abstract VisionResult Execute(Mat inputImage);

        /// <summary>
        /// ROI가 설정된 경우 해당 영역만 추출
        /// </summary>
        protected Mat GetROIImage(Mat inputImage)
        {
            if (!UseROI) return inputImage.Clone();

            // 1. 컨셉 2단계: 항상 보정된(정규화된) ROI를 기준으로 함
            var adjustedROI = GetAdjustedROI(inputImage);

            // 2. 컨셉 3단계: 보정된 좌표로 Crop 수행
            if (adjustedROI.Width <= 0 || adjustedROI.Height <= 0)
                return inputImage.Clone();

            return new Mat(inputImage, adjustedROI);
        }

        protected Rect GetAdjustedROI(Mat inputImage)
        {
            // 1. 역방향 드래그 등으로 인한 음수 Width/Height 보정 (Normalization)
            int x1 = ROI.X;
            int y1 = ROI.Y;
            int x2 = ROI.X + ROI.Width;
            int y2 = ROI.Y + ROI.Height;

            int minX = Math.Min(x1, x2);
            int maxX = Math.Max(x1, x2);
            int minY = Math.Min(y1, y2);
            int maxY = Math.Max(y1, y2);

            // 2. 이미지 영역과 겹치는 구간(Intersection) 계산
            int startX = Math.Clamp(minX, 0, inputImage.Width);
            int startY = Math.Clamp(minY, 0, inputImage.Height);
            int endX = Math.Clamp(maxX, 0, inputImage.Width);
            int endY = Math.Clamp(maxY, 0, inputImage.Height);

            // 3. 최종 Width/Height 계산 (최소 0 보장)
            return new Rect(startX, startY, endX - startX, endY - startY);
        }

        /// <summary>
        /// 처리된 ROI 결과를 원본 이미지 크기에 맞게 적용
        /// ROI 외부 영역은 검은색(또는 지정된 색상)으로 채움
        /// </summary>
        /// <param name="inputImage">원본 입력 이미지</param>
        /// <param name="processedROI">처리된 ROI 이미지</param>
        /// <param name="fillColor">ROI 외부 영역을 채울 색상 (기본값: 검은색)</param>
        /// <returns>원본 크기의 이미지 (ROI 영역만 처리 결과 포함)</returns>
        protected Mat ApplyROIResult(Mat inputImage, Mat processedROI, Scalar? fillColor = null)
        {
            var adjustedROI = GetAdjustedROI(inputImage);

            // 원본 ROI.Width 대신 adjustedROI.Width를 체크하여 역방향 드래그 대응
            if (!UseROI || adjustedROI.Width <= 0 || adjustedROI.Height <= 0)
                return processedROI.Clone();

            var resultImage = new Mat(inputImage.Size(), processedROI.Type(), fillColor ?? Scalar.Black);
            var destRegion = new Mat(resultImage, adjustedROI);

            // processedROI 크기가 adjustedROI와 다를 수 있으므로 크기 맞춤
            if (processedROI.Width == adjustedROI.Width && processedROI.Height == adjustedROI.Height)
            {
                processedROI.CopyTo(destRegion);
            }
            else
            {
                // 크기가 다른 경우 리사이즈
                var resized = new Mat();
                Cv2.Resize(processedROI, resized, new Size(adjustedROI.Width, adjustedROI.Height));
                resized.CopyTo(destRegion);
                resized.Dispose();
            }

            return resultImage;
        }

        /// <summary>
        /// 도구의 복제본 생성
        /// </summary>
        public abstract VisionToolBase Clone();
    }

    /// <summary>
    /// 비전 처리 결과
    /// </summary>
    public class VisionResult : ObservableObject
    {
        private bool _success;
        public bool Success
        {
            get => _success;
            set => SetProperty(ref _success, value);
        }

        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        private Mat? _outputImage;
        public Mat? OutputImage
        {
            get => _outputImage;
            set => SetProperty(ref _outputImage, value);
        }

        private Mat? _overlayImage;
        public Mat? OverlayImage
        {
            get => _overlayImage;
            set => SetProperty(ref _overlayImage, value);
        }

        // 결과 데이터 (측정값, 좌표 등)
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        // Graphics 오버레이 정보
        public List<GraphicOverlay> Graphics { get; set; } = new List<GraphicOverlay>();
    }

    /// <summary>
    /// 결과 표시를 위한 그래픽 오버레이
    /// </summary>
    public class GraphicOverlay
    {
        public GraphicType Type { get; set; }
        public Point2d Position { get; set; }
        public Point2d EndPosition { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Radius { get; set; }
        public double Angle { get; set; }
        public Scalar Color { get; set; } = new Scalar(0, 255, 0);
        public int Thickness { get; set; } = 2;
        public string? Text { get; set; }
        public List<Point>? Points { get; set; }
    }

    public enum GraphicType
    {
        Point,
        Line,
        Rectangle,
        Circle,
        Ellipse,
        Polygon,
        Text,
        Crosshair
    }
}
