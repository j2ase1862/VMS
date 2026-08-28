using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VMS.PLC.Models;
using VMS.VisionSetup.Services;

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
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 외부 실행 엔진이 오버레이 렌더링용 원본 이미지를 주입하기 위한 속성
        /// </summary>
        public Mat? OverlayBaseImage { get; set; }

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
                    // 사용자가 ROI를 변경한 경우 Fixture 기준점 리셋
                    // (Fixture Transform 적용 중에는 리셋하지 않음)
                    if (!IsFixtureTransformActive)
                        HasFixtureBaseROI = false;

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

        // ROI 회전 각도 (RectangleAffineROI 사용 시 저장/직렬화용)
        private double _roiAngle;
        public double ROIAngle
        {
            get => _roiAngle;
            set => SetProperty(ref _roiAngle, value);
        }

        // ROI 회전 중심 좌표 (RectangleAffineROI 사용 시)
        private double _roiCenterX;
        public double ROICenterX
        {
            get => _roiCenterX;
            set => SetProperty(ref _roiCenterX, value);
        }

        private double _roiCenterY;
        public double ROICenterY
        {
            get => _roiCenterY;
            set => SetProperty(ref _roiCenterY, value);
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
            set
            {
                if (SetProperty(ref _useROI, value) && !IsFixtureTransformActive)
                    HasFixtureBaseROI = false;
            }
        }

        /// <summary>
        /// Fixture Transform 적용 중 플래그.
        /// true일 때 ROI/UseROI 변경이 HasFixtureBaseROI를 리셋하지 않음.
        /// </summary>
        public bool IsFixtureTransformActive { get; set; }

        public Rect FixtureBaseROI { get; set; }
        public bool HasFixtureBaseROI { get; set; }
        public double FixtureRefX { get; set; }      // 최초 실행 시 FeatureMatch foundX
        public double FixtureRefY { get; set; }      // 최초 실행 시 FeatureMatch foundY
        public double FixtureRefAngle { get; set; }  // 최초 실행 시 FeatureMatch angle

        // SearchRegion(Execute용)도 동일한 Fixture 변환을 받기 위한 base.
        // ISearchRegionTool 구현 도구가 사용. ROI(Training Region)와 별개로 관리.
        public Rect FixtureBaseSearchRegion { get; set; }
        public bool HasFixtureBaseSearchRegion { get; set; }

        // 마지막 실행 결과
        private VisionResult? _lastResult;
        public VisionResult? LastResult
        {
            get => _lastResult;
            set
            {
                // 도구 내부에서 set한 결과를 VisionService가 같은 인스턴스로 다시 set하는 경우가 있음 — 무시
                if (ReferenceEquals(_lastResult, value))
                    return;

                // 교체되는 이전 결과의 Mat(OutputImage/OverlayImage)을 해제해 네이티브 메모리 누수 방지.
                // Results/LastExecutionResultsById가 같은 인스턴스를 공유할 수 있으나
                // ReleaseMats()는 idempotent라 중복 호출에 안전.
                // 표시 경로(MainViewModel)는 표시 시점에 ToWriteableBitmap()/Clone() 복사본을 만들므로
                // 이전 결과의 원본 Mat 해제는 화면 표시와 무관하다.
                _lastResult?.ReleaseMats();
                SetProperty(ref _lastResult, value);
            }
        }

        // 실행 시간 (ms)
        private double _executionTime;
        public double ExecutionTime
        {
            get => _executionTime;
            set => SetProperty(ref _executionTime, value);
        }

        // Web 파라미터 연동 (PropertyName → ParamCode)
        /// <summary>
        /// Web에서 동기화된 파라미터 코드를 도구 프로퍼티에 매핑.
        /// Key: 프로퍼티 이름 (예: "ThresholdValue"), Value: ParamCode (정수)
        /// </summary>
        public Dictionary<string, int> LinkedParamCodes { get; set; } = new();

        // PLC 결과 전송 설정 (1:N 매핑)
        /// <summary>
        /// PLC 결과 매핑 리스트. 하나의 도구에서 여러 결과를 서로 다른 PLC 주소로 전송 가능.
        /// </summary>
        public ObservableCollection<PlcResultMapping> PlcMappings { get; set; } = new();

        /// <summary>
        /// 도구가 제공하는 결과 키 목록 (UI 콤보 박스용).
        /// 파생 클래스에서 오버라이드하여 도구별 키를 반환.
        /// </summary>
        public virtual List<string> GetAvailableResultKeys()
        {
            return new List<string> { "Success" };
        }

        /// <summary>
        /// PlcMappings를 대상 도구에 깊은 복사.
        /// Clone() 구현에서 호출.
        /// </summary>
        public void CopyPlcMappingsTo(VisionToolBase target)
        {
            foreach (var mapping in PlcMappings)
            {
                target.PlcMappings.Add(new PlcResultMapping
                {
                    ResultKey = mapping.ResultKey,
                    DeviceId = mapping.DeviceId,
                    PlcAddress = mapping.PlcAddress,
                    DataType = mapping.DataType
                });
            }
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
        /// 오버레이 렌더링용 컬러 이미지 획득.
        /// OverlayBaseImage → VisionService.CurrentImage → inputImage 순으로 fallback.
        /// 항상 BGR 3채널 Mat을 반환.
        /// </summary>
        protected Mat GetColorOverlayBase(Mat inputImage)
        {
            var orig = OverlayBaseImage ?? VisionService.Instance.CurrentImage;
            if (orig != null && !orig.Empty()
                && orig.Width == inputImage.Width && orig.Height == inputImage.Height)
            {
                return orig.Channels() >= 3
                    ? orig.Clone()
                    : orig.CvtColor(ColorConversionCodes.GRAY2BGR);
            }
            return inputImage.Channels() >= 3
                ? inputImage.Clone()
                : inputImage.CvtColor(ColorConversionCodes.GRAY2BGR);
        }

        /// <summary>
        /// 도구 실행 - 파생 클래스에서 구현
        /// </summary>
        public abstract VisionResult Execute(Mat inputImage);

        /// <summary>
        /// GetROIImage()로 얻은 cropped workImage에 비사각형 ROI 도형 마스크를 in-place 적용.
        /// 도형 밖 영역을 fillColor로 칠해서, 마스크를 출력에 활용 안 하는 도구(CodeReader, OCR 등)도
        /// Circle/Ellipse/Polygon ROI를 존중하도록 함. 사각형 ROI 또는 ROI 미사용 시 no-op.
        /// </summary>
        /// <param name="workImage">GetROIImage()로 얻은 cropped Mat (bounding rect 기준)</param>
        /// <param name="inputImage">원본 입력 이미지(크기 참조용)</param>
        /// <param name="fillColor">도형 밖을 채울 색상(기본: 흰색 — 인식기 친화)</param>
        protected void ApplyShapeMaskInPlace(Mat workImage, Mat inputImage, Scalar? fillColor = null)
        {
            using var fullMask = GetNonRectangularShapeMask(inputImage);
            if (fullMask == null) return;

            var adjROI = GetAdjustedROI(inputImage);
            if (adjROI.Width <= 0 || adjROI.Height <= 0) return;

            using var cropMask = new Mat(fullMask, adjROI);
            if (cropMask.Size() != workImage.Size()) return;

            using var invMask = new Mat();
            Cv2.BitwiseNot(cropMask, invMask);
            workImage.SetTo(fillColor ?? Scalar.White, invMask);
        }

        /// <summary>
        /// AssociatedROIShape이 비사각형(Circle/Ellipse/Polygon)일 때만 입력 이미지 크기의 채우기 마스크 반환.
        /// 사각형(Rectangle/RectangleAffine)이거나 ROI 미사용이면 null.
        /// 호출자는 반환 값을 Dispose해야 함.
        /// 도구는 이 마스크를 활용해 contour 검출/binary 결과가 실제 도형 안에 한정되도록 함.
        /// </summary>
        protected Mat? GetNonRectangularShapeMask(Mat inputImage)
        {
            if (!UseROI) return null;
            var shape = AssociatedROIShape;
            if (shape == null) return null;
            if (shape is RectangleROI || shape is RectangleAffineROI) return null;
            return shape.CreateMask(inputImage.Width, inputImage.Height);
        }

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

        /// <summary>
        /// 회전 ROI(RectangleAffineROI)를 존중하는 ROI 추출.
        /// 각도가 있으면 회전 정렬 워프(CodeReaderTool.ExtractAffineROI와 동일 규약) 후
        /// 축 정렬 크롭 — 결과는 ROI 내용이 수평으로 펴진 이미지.
        /// 각도가 없으면 GetROIImage()와 동일. 호출자가 반환 Mat을 소유(Dispose 책임).
        /// 캔버스 각도는 화면 시계방향(+), OpenCV는 반시계방향(+)이라 +angle 워프가 정렬이다.
        /// </summary>
        public Mat GetAlignedROIImage(Mat inputImage)
        {
            double angle = AssociatedROIShape is RectangleAffineROI liveROI ? liveROI.Angle : ROIAngle;

            if (!UseROI || ROI.Width <= 0 || ROI.Height <= 0 || Math.Abs(angle) <= 0.001)
                return GetROIImage(inputImage);

            double cx = ROICenterX != 0 ? ROICenterX : ROI.X + ROI.Width / 2.0;
            double cy = ROICenterY != 0 ? ROICenterY : ROI.Y + ROI.Height / 2.0;
            var center = new Point2f((float)cx, (float)cy);

            using var rotMat = Cv2.GetRotationMatrix2D(center, angle, 1.0);
            using var rotated = new Mat();
            Cv2.WarpAffine(inputImage, rotated, rotMat, inputImage.Size(),
                InterpolationFlags.Linear, BorderTypes.Replicate);

            int x = (int)(cx - ROI.Width / 2.0);
            int y = (int)(cy - ROI.Height / 2.0);
            int x1 = Math.Clamp(x, 0, rotated.Width);
            int y1 = Math.Clamp(y, 0, rotated.Height);
            int x2 = Math.Clamp(x + ROI.Width, 0, rotated.Width);
            int y2 = Math.Clamp(y + ROI.Height, 0, rotated.Height);

            if (x2 - x1 <= 0 || y2 - y1 <= 0)
                return inputImage.Clone();

            return new Mat(rotated, new Rect(x1, y1, x2 - x1, y2 - y1)).Clone();
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

            // 비사각형 ROI(Circle/Ellipse/Polygon)일 때, 도형 밖 영역을 fillColor로 마스킹.
            // 회전 사각형은 도구별 특수 처리되므로 여기 도달하지 않음(GetNonRectangularShapeMask가 null 반환).
            using var shapeMask = GetNonRectangularShapeMask(inputImage);
            if (shapeMask != null && !shapeMask.Empty()
                && shapeMask.Width == resultImage.Width
                && shapeMask.Height == resultImage.Height)
            {
                using var invMask = new Mat();
                Cv2.BitwiseNot(shapeMask, invMask);
                resultImage.SetTo(fillColor ?? Scalar.Black, invMask);
            }

            return resultImage;
        }

        /// <summary>
        /// 도구의 복제본 생성
        /// </summary>
        public abstract VisionToolBase Clone();

        // ────────────────────────────────────────────────────────────
        // 캘리브레이션 기반 mm 변환 헬퍼 (측정 도구가 결과 Data에 mm 키를 함께 노출하기 위함)
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// VisionService.CurrentCalibrationMetadata가 있으면 (xKey, yKey) 픽셀 좌표를 mm 좌표로 변환해
        /// {xKey}Mm, {yKey}Mm 키로 추가. Homography가 있으면 perspective 변환, 없으면 등방 스케일링.
        /// 캘리브레이션이 없거나 키가 없으면 no-op.
        /// </summary>
        protected static void AddCoordMm(VisionResult result, string xKey, string yKey, CalibrationMetadata? cal)
        {
            if (cal == null) return;
            if (!result.Data.TryGetValue(xKey, out var xObj) || !result.Data.TryGetValue(yKey, out var yObj))
                return;
            if (!TryToDouble(xObj, out var x) || !TryToDouble(yObj, out var y)) return;
            var (xMm, yMm) = cal.PixelToMm(x, y);
            result.Data[xKey + "Mm"] = xMm;
            result.Data[yKey + "Mm"] = yMm;
        }

        /// <summary>
        /// 스칼라 픽셀 길이를 mm로 변환해 {key}Mm 키로 추가 (등방 PixelSizeMm 가정).
        /// </summary>
        protected static void AddLengthMm(VisionResult result, string key, CalibrationMetadata? cal)
        {
            if (cal == null) return;
            if (!result.Data.TryGetValue(key, out var obj)) return;
            if (!TryToDouble(obj, out var v)) return;
            result.Data[key + "Mm"] = cal.LengthToMm(v);
        }

        private static bool TryToDouble(object obj, out double value)
        {
            switch (obj)
            {
                case double d: value = d; return true;
                case float f: value = f; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                default: value = 0; return false;
            }
        }
    }

    /// <summary>
    /// Execute용 Search Region을 별도로 갖는 도구. Training Region(UseROI/ROI)와 분리되어
    /// FeatureMatchTool 등의 fixture 변환을 받을 때 SearchRegion도 함께 시프트되도록 함.
    /// </summary>
    public interface ISearchRegionTool
    {
        bool UseSearchRegion { get; set; }
        Rect SearchRegion { get; set; }
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

        /// <summary>
        /// 보유한 Mat 리소스(OutputImage/OverlayImage) 해제.
        /// 파이프라인 재실행 시 이전 결과 정리용 — Results/LastExecutionResultsById/도구별 LastResult가
        /// 동일 인스턴스를 공유하므로 중복 호출에 안전하도록 idempotent(해제 후 null)로 구현.
        /// Data/Message는 유지되므로 결과 텍스트를 참조하는 곳(ChatViewModel 등)은 영향 없음.
        /// </summary>
        public void ReleaseMats()
        {
            // 필드 직접 접근: 해제 알림으로 UI 바인딩을 흔들지 않기 위해 SetProperty 미사용
            var output = _outputImage;
            _outputImage = null;
            output?.Dispose();

            var overlay = _overlayImage;
            _overlayImage = null;
            overlay?.Dispose();
        }
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
