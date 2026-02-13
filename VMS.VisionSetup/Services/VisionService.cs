using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 비전 처리 서비스
    /// Cognex VisionPro의 CogJobManager 역할을 대체
    /// </summary>
    public class VisionService : ObservableObject, IVisionService
    {
        private static VisionService? _instance;
        public static VisionService Instance => _instance ??= new VisionService();

        // 현재 이미지
        private Mat? _currentImage;
        public Mat? CurrentImage
        {
            get => _currentImage;
            set
            {
                _currentImage?.Dispose();
                SetProperty(ref _currentImage, value);
                UpdateDisplayImage();
            }
        }

        // 화면 표시용 이미지
        private ImageSource? _displayImage;
        public ImageSource? DisplayImage
        {
            get => _displayImage;
            private set => SetProperty(ref _displayImage, value);
        }

        // 오버레이 이미지
        private ImageSource? _overlayImage;
        public ImageSource? OverlayImage
        {
            get => _overlayImage;
            private set => SetProperty(ref _overlayImage, value);
        }

        // 도구 목록
        public ObservableCollection<VisionToolBase> Tools { get; } = new();

        // 실행 결과 목록
        public ObservableCollection<VisionResult> Results { get; } = new();

        // 도구 간 연결 정보
        private readonly List<ToolConnectionInfo> _connections = new();

        /// <summary>
        /// 도구 간 연결 정보 (내부용)
        /// ID 기반 매칭으로 백그라운드 스레드에서도 안정적으로 동작
        /// </summary>
        private class ToolConnectionInfo
        {
            public string SourceId { get; set; } = string.Empty;
            public string TargetId { get; set; } = string.Empty;
            public ConnectionType Type { get; set; }
        }

        // 전체 실행 시간
        private double _totalExecutionTime;
        public double TotalExecutionTime
        {
            get => _totalExecutionTime;
            private set => SetProperty(ref _totalExecutionTime, value);
        }

        // 실행 상태
        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set => SetProperty(ref _isRunning, value);
        }

        // 마지막 실행 성공 여부
        private bool _lastRunSuccess;
        public bool LastRunSuccess
        {
            get => _lastRunSuccess;
            private set => SetProperty(ref _lastRunSuccess, value);
        }

        // 합성 오버레이 이미지 (모든 도구의 그래픽을 하나로 합성)
        private Mat? _lastCompositeOverlay;
        public Mat? LastCompositeOverlay
        {
            get => _lastCompositeOverlay;
            private set
            {
                _lastCompositeOverlay?.Dispose();
                _lastCompositeOverlay = value;
            }
        }

        private VisionService() { }

        /// <summary>
        /// 이미지 파일 로드
        /// </summary>
        public bool LoadImage(string filePath)
        {
            try
            {
                var image = Cv2.ImRead(filePath);
                if (image.Empty())
                    return false;

                CurrentImage = image;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Mat 이미지 설정
        /// </summary>
        public void SetImage(Mat image)
        {
            CurrentImage = image.Clone();
        }

        /// <summary>
        /// 화면 표시 이미지 업데이트
        /// </summary>
        private void UpdateDisplayImage()
        {
            if (CurrentImage == null || CurrentImage.Empty())
            {
                DisplayImage = null;
                return;
            }

            try
            {
                DisplayImage = CurrentImage.ToWriteableBitmap();
            }
            catch
            {
                DisplayImage = null;
            }
        }

        /// <summary>
        /// 도구 추가
        /// </summary>
        public void AddTool(VisionToolBase tool)
        {
            Tools.Add(tool);
        }

        /// <summary>
        /// 도구 제거
        /// </summary>
        public void RemoveTool(VisionToolBase tool)
        {
            Tools.Remove(tool);
        }

        /// <summary>
        /// 도구 순서 변경
        /// </summary>
        public void MoveTool(int fromIndex, int toIndex)
        {
            if (fromIndex >= 0 && fromIndex < Tools.Count &&
                toIndex >= 0 && toIndex < Tools.Count)
            {
                Tools.Move(fromIndex, toIndex);
            }
        }

        /// <summary>
        /// 모든 도구 제거
        /// </summary>
        public void ClearTools()
        {
            Tools.Clear();
            _connections.Clear();
        }

        #region Connection Management

        /// <summary>
        /// 도구 간 연결 추가
        /// </summary>
        public void AddConnection(VisionToolBase source, VisionToolBase target, ConnectionType type)
        {
            // 중복 방지 (ID 기반)
            if (_connections.Any(c => c.SourceId == source.Id && c.TargetId == target.Id && c.Type == type))
                return;

            _connections.Add(new ToolConnectionInfo
            {
                SourceId = source.Id,
                TargetId = target.Id,
                Type = type
            });
        }

        /// <summary>
        /// 도구 간 연결 제거
        /// </summary>
        public void RemoveConnection(VisionToolBase source, VisionToolBase target, ConnectionType type)
        {
            _connections.RemoveAll(c => c.SourceId == source.Id && c.TargetId == target.Id && c.Type == type);
        }

        /// <summary>
        /// 모든 연결 제거
        /// </summary>
        public void ClearConnections()
        {
            _connections.Clear();
        }

        /// <summary>
        /// 특정 도구에 대한 입력 연결 가져오기 (해당 도구가 Target인 연결들)
        /// </summary>
        private List<ToolConnectionInfo> GetInputConnections(VisionToolBase tool)
        {
            return _connections.Where(c => c.TargetId == tool.Id).ToList();
        }

        /// <summary>
        /// 연결 정보를 기반으로 도구의 입력 이미지 결정
        /// ID 기반으로 Source 도구를 찾아 결과 이미지를 반환
        /// </summary>
        private Mat? GetConnectedInputImage(VisionToolBase tool, Dictionary<string, VisionResult> resultMap)
        {
            var imageConnection = _connections
                .FirstOrDefault(c => c.TargetId == tool.Id && c.Type == ConnectionType.Image);

            if (imageConnection != null && resultMap.TryGetValue(imageConnection.SourceId, out var sourceResult))
            {
                // Image 연결: Source 도구의 출력 이미지를 직접 공유 (Clone 제거)
                // Execute()는 입력을 수정하지 않으므로 안전하게 공유 가능
                if (sourceResult.OutputImage != null && !sourceResult.OutputImage.Empty())
                    return sourceResult.OutputImage;
            }

            return null;
        }

        /// <summary>
        /// Result 연결 확인: 연결된 Source 도구의 결과가 실패이면 실행 건너뛰기
        /// </summary>
        private bool ShouldSkipByResultConnection(VisionToolBase tool, Dictionary<string, VisionResult> resultMap)
        {
            var resultConnections = _connections
                .Where(c => c.TargetId == tool.Id && c.Type == ConnectionType.Result)
                .ToList();

            foreach (var conn in resultConnections)
            {
                if (resultMap.TryGetValue(conn.SourceId, out var sourceResult))
                {
                    // Result 연결: Source가 실패이면 Target도 건너뜀
                    if (!sourceResult.Success)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Coordinates 연결: Source 도구의 좌표 데이터를 Target 도구에 적용
        /// FeatureMatchTool이 소스인 경우 Fixture 변환 (학습 위치 대비 델타 적용)
        /// </summary>
        private void ApplyCoordinatesConnection(VisionToolBase tool, Dictionary<string, VisionResult> resultMap)
        {
            var coordConnections = _connections
                .Where(c => c.TargetId == tool.Id && c.Type == ConnectionType.Coordinates)
                .ToList();

            foreach (var conn in coordConnections)
            {
                if (!resultMap.TryGetValue(conn.SourceId, out var sourceResult) || sourceResult.Data == null)
                    continue;

                // Save user-configured ROI on first fixture application
                if (!tool.HasFixtureBaseROI)
                {
                    tool.FixtureBaseROI = tool.ROI;
                    tool.HasFixtureBaseROI = true;
                }

                // Fixture transform: trained center available → apply delta
                if (sourceResult.Data.TryGetValue("CenterX", out var cx) &&
                    sourceResult.Data.TryGetValue("CenterY", out var cy) &&
                    sourceResult.Data.TryGetValue("TrainedCenterX", out var tcx) &&
                    sourceResult.Data.TryGetValue("TrainedCenterY", out var tcy))
                {
                    double foundX = Convert.ToDouble(cx);
                    double foundY = Convert.ToDouble(cy);
                    double trainedX = Convert.ToDouble(tcx);
                    double trainedY = Convert.ToDouble(tcy);

                    double baseCX = tool.FixtureBaseROI.X + tool.FixtureBaseROI.Width / 2.0;
                    double baseCY = tool.FixtureBaseROI.Y + tool.FixtureBaseROI.Height / 2.0;

                    double deltaAngle = 0;
                    if (sourceResult.Data.TryGetValue("Angle", out var angleObj))
                        deltaAngle = Convert.ToDouble(angleObj);

                    double newCX, newCY;
                    if (Math.Abs(deltaAngle) > 0.01)
                    {
                        // Rotate ROI center around trained center, then translate to found center
                        double relX = baseCX - trainedX;
                        double relY = baseCY - trainedY;
                        double rad = deltaAngle * Math.PI / 180.0;
                        newCX = foundX + relX * Math.Cos(rad) - relY * Math.Sin(rad);
                        newCY = foundY + relX * Math.Sin(rad) + relY * Math.Cos(rad);
                    }
                    else
                    {
                        // Translation only
                        newCX = baseCX + (foundX - trainedX);
                        newCY = baseCY + (foundY - trainedY);
                    }

                    int w = tool.FixtureBaseROI.Width > 0 ? tool.FixtureBaseROI.Width : 100;
                    int h = tool.FixtureBaseROI.Height > 0 ? tool.FixtureBaseROI.Height : 100;
                    tool.ROI = new Rect((int)(newCX - w / 2.0), (int)(newCY - h / 2.0), w, h);
                    tool.UseROI = true;
                }
                // Fallback: BoundingRect
                else if (sourceResult.Data.TryGetValue("BoundingRect", out var rectObj) && rectObj is Rect boundingRect)
                {
                    tool.ROI = boundingRect;
                    tool.UseROI = true;
                }
                // Fallback: center-based (non-fixture sources)
                else if (sourceResult.Data.TryGetValue("CenterX", out var fcx) &&
                         sourceResult.Data.TryGetValue("CenterY", out var fcy))
                {
                    double centerX = Convert.ToDouble(fcx);
                    double centerY = Convert.ToDouble(fcy);
                    int roiW = tool.ROI.Width > 0 ? tool.ROI.Width : 100;
                    int roiH = tool.ROI.Height > 0 ? tool.ROI.Height : 100;
                    tool.ROI = new Rect((int)(centerX - roiW / 2), (int)(centerY - roiH / 2), roiW, roiH);
                    tool.UseROI = true;
                }
            }
        }

        #endregion

        #region Topological Sort

        /// <summary>
        /// 연결 의존성에 따라 도구를 위상 정렬 (소스가 타겟보다 먼저 실행되도록)
        /// </summary>
        private List<VisionToolBase> TopologicalSort(IEnumerable<VisionToolBase> tools)
        {
            var toolList = tools.ToList();
            var toolById = toolList.ToDictionary(t => t.Id);

            // Build adjacency: for each tool, which tools depend on it (outgoing edges)
            var dependents = new Dictionary<string, List<string>>();
            var inDegree = new Dictionary<string, int>();
            foreach (var t in toolList)
            {
                dependents[t.Id] = new List<string>();
                inDegree[t.Id] = 0;
            }

            foreach (var conn in _connections)
            {
                if (toolById.ContainsKey(conn.SourceId) && toolById.ContainsKey(conn.TargetId))
                {
                    dependents[conn.SourceId].Add(conn.TargetId);
                    inDegree[conn.TargetId]++;
                }
            }

            // Kahn's algorithm
            var queue = new Queue<string>();
            foreach (var t in toolList)
                if (inDegree[t.Id] == 0)
                    queue.Enqueue(t.Id);

            var sorted = new List<VisionToolBase>();
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                sorted.Add(toolById[id]);
                foreach (var depId in dependents[id])
                {
                    inDegree[depId]--;
                    if (inDegree[depId] == 0)
                        queue.Enqueue(depId);
                }
            }

            // If cycle detected (sorted.Count < toolList.Count), append remaining in original order
            if (sorted.Count < toolList.Count)
            {
                var sortedIds = new HashSet<string>(sorted.Select(t => t.Id));
                foreach (var t in toolList)
                    if (!sortedIds.Contains(t.Id))
                        sorted.Add(t);
            }

            return sorted;
        }

        /// <summary>
        /// 지정된 도구의 모든 업스트림 의존성을 재귀적으로 수집 (연결된 소스 도구들)
        /// </summary>
        private List<VisionToolBase> GetUpstreamDependencies(VisionToolBase tool)
        {
            var visited = new HashSet<string>();
            var result = new List<VisionToolBase>();
            var toolById = Tools.ToDictionary(t => t.Id);
            CollectUpstream(tool.Id, toolById, visited, result);
            return result;
        }

        private void CollectUpstream(string toolId, Dictionary<string, VisionToolBase> toolById,
            HashSet<string> visited, List<VisionToolBase> result)
        {
            var incoming = _connections.Where(c => c.TargetId == toolId).ToList();
            foreach (var conn in incoming)
            {
                if (visited.Contains(conn.SourceId)) continue;
                visited.Add(conn.SourceId);
                if (toolById.TryGetValue(conn.SourceId, out var source))
                {
                    CollectUpstream(conn.SourceId, toolById, visited, result);
                    result.Add(source);
                }
            }
        }

        #endregion

        /// <summary>
        /// 오버레이에서 그래픽만 추출하여 합성 이미지에 병합
        /// 오버레이와 정확히 동일한 입력 이미지의 차이(그래픽 부분)만 합성 이미지에 복사
        /// </summary>
        private static void MergeOverlayGraphics(Mat overlay, Mat baseInput, Mat composite)
        {
            Mat overlayBGR = overlay;
            Mat inputBGR = baseInput;
            bool disposeOverlay = false, disposeInput = false;

            if (overlay.Channels() == 1)
            {
                overlayBGR = new Mat();
                Cv2.CvtColor(overlay, overlayBGR, ColorConversionCodes.GRAY2BGR);
                disposeOverlay = true;
            }
            if (baseInput.Channels() == 1)
            {
                inputBGR = new Mat();
                Cv2.CvtColor(baseInput, inputBGR, ColorConversionCodes.GRAY2BGR);
                disposeInput = true;
            }

            try
            {
                if (overlayBGR.Size() != composite.Size()) return;

                using var diff = new Mat();
                Cv2.Absdiff(overlayBGR, inputBGR, diff);
                using var grayDiff = new Mat();
                Cv2.CvtColor(diff, grayDiff, ColorConversionCodes.BGR2GRAY);
                using var mask = new Mat();
                Cv2.Threshold(grayDiff, mask, 1, 255, ThresholdTypes.Binary);
                overlayBGR.CopyTo(composite, mask);
            }
            finally
            {
                if (disposeOverlay) overlayBGR.Dispose();
                if (disposeInput) inputBGR.Dispose();
            }
        }

        /// <summary>
        /// 단일 도구 실행 (연결된 업스트림 도구가 있으면 자동으로 먼저 실행)
        /// </summary>
        public VisionResult ExecuteTool(VisionToolBase tool, Mat? inputImage = null)
        {
            var image = inputImage ?? CurrentImage;
            if (image == null || image.Empty())
            {
                return new VisionResult
                {
                    Success = false,
                    Message = "입력 이미지가 없습니다."
                };
            }

            if (!tool.IsEnabled)
            {
                return new VisionResult
                {
                    Success = true,
                    Message = "도구가 비활성화되어 있습니다.",
                    OutputImage = image.Clone()
                };
            }

            // Run upstream dependencies first so connected input is available
            var upstream = GetUpstreamDependencies(tool);
            var resultMap = new Dictionary<string, VisionResult>();

            foreach (var dep in upstream)
            {
                if (!dep.IsEnabled) continue;

                Mat depInput;
                var depConnected = GetConnectedInputImage(dep, resultMap);
                if (depConnected != null)
                    depInput = depConnected;
                else
                    depInput = image.Clone();

                try
                {
                    var depResult = dep.Execute(depInput);
                    dep.LastResult = depResult;
                    resultMap[dep.Id] = depResult;
                }
                finally
                {
                    depInput.Dispose();
                }
            }

            // Resolve connected input for the target tool
            var connectedImage = GetConnectedInputImage(tool, resultMap);
            Mat toolInput;
            if (connectedImage != null)
                toolInput = connectedImage;
            else
                toolInput = image.Clone();

            try
            {
                var result = tool.Execute(toolInput);
                tool.LastResult = result;
                return result;
            }
            finally
            {
                toolInput.Dispose();
            }
        }

        /// <summary>
        /// 모든 도구 순차 실행
        /// </summary>
        public async Task<List<VisionResult>> ExecuteAllAsync()
        {
            return await Task.Run(() => ExecuteAll());
        }

        /// <summary>
        /// 모든 도구 순차 실행 (동기)
        /// Image 연결이 있는 경우 연결된 소스의 출력 이미지를 입력으로 사용하고,
        /// 연결이 없는 경우 원본 이미지를 입력으로 사용
        /// </summary>
        public List<VisionResult> ExecuteAll()
        {
            Results.Clear();
            var results = new List<VisionResult>();

            if (CurrentImage == null || CurrentImage.Empty())
            {
                var errorResult = new VisionResult
                {
                    Success = false,
                    Message = "입력 이미지가 없습니다."
                };
                results.Add(errorResult);
                Results.Add(errorResult);
                LastRunSuccess = false;
                return results;
            }

            IsRunning = true;
            var sw = Stopwatch.StartNew();

            // 각 도구의 실행 결과를 추적 (ID 기반, 연결 데이터 전달용)
            var resultMap = new Dictionary<string, VisionResult>();
            bool allSuccess = true;
            Mat? compositeOverlay = null;
            LastCompositeOverlay = null;

            // Pre-compute grayscale for the base image once
            Mat? baseGray = null;
            if (CurrentImage.Channels() > 1)
            {
                baseGray = CurrentImage.CvtColor(ColorConversionCodes.BGR2GRAY);
            }

            // Track grayscale conversions for connected sources to avoid duplicates
            var connectedGrayCache = new Dictionary<string, Mat>();

            // Topological sort: ensures sources execute before their targets
            var sortedTools = TopologicalSort(Tools);

            foreach (var tool in sortedTools)
            {
                if (!tool.IsEnabled)
                    continue;

                // 1. Result 연결 확인: Source가 실패이면 건너뛰기
                if (ShouldSkipByResultConnection(tool, resultMap))
                {
                    var skipResult = new VisionResult
                    {
                        Success = false,
                        Message = $"연결된 도구의 결과가 실패하여 건너뜀: {tool.Name}"
                    };
                    results.Add(skipResult);
                    Results.Add(skipResult);
                    resultMap[tool.Id] = skipResult;
                    allSuccess = false;
                    continue;
                }

                // 2. Coordinates 연결: Source의 좌표 데이터를 현재 도구에 적용
                ApplyCoordinatesConnection(tool, resultMap);

                // 3. Image 연결: 연결된 Source의 출력 이미지를 입력으로 사용
                //    연결이 없으면 원본 이미지 사용 (각 도구가 독립적으로 원본 처리)
                Mat inputImage;
                var connectedImage = GetConnectedInputImage(tool, resultMap);
                bool usesBaseImage = connectedImage == null;
                if (connectedImage != null)
                {
                    inputImage = connectedImage;

                    // Set grayscale cache for connected image sources
                    var imageConn = _connections
                        .FirstOrDefault(c => c.TargetId == tool.Id && c.Type == ConnectionType.Image);
                    if (imageConn != null && inputImage.Channels() > 1)
                    {
                        if (!connectedGrayCache.TryGetValue(imageConn.SourceId, out var cachedGray))
                        {
                            cachedGray = inputImage.CvtColor(ColorConversionCodes.BGR2GRAY);
                            connectedGrayCache[imageConn.SourceId] = cachedGray;
                        }
                        tool.SetCachedGrayscale(cachedGray.Clone());
                    }
                }
                else
                {
                    inputImage = CurrentImage.Clone();
                    if (baseGray != null)
                        tool.SetCachedGrayscale(baseGray.Clone());
                }

                try
                {
                    var result = tool.Execute(inputImage);
                    tool.LastResult = result;
                    results.Add(result);
                    Results.Add(result);
                    resultMap[tool.Id] = result;

                    if (!result.Success)
                    {
                        allSuccess = false;
                    }

                    // 합성 오버레이 구축: 각 도구의 그래픽을 하나의 이미지에 합성
                    // Overlays are drawn on CurrentImage (original color) via GetColorOverlayBase,
                    // so use CurrentImage as the diff base to correctly extract only graphics pixels.
                    if (result.OverlayImage != null && !result.OverlayImage.Empty())
                    {
                        if (compositeOverlay == null)
                        {
                            compositeOverlay = result.OverlayImage.Clone();
                            if (compositeOverlay.Channels() == 1)
                                Cv2.CvtColor(compositeOverlay, compositeOverlay, ColorConversionCodes.GRAY2BGR);
                        }
                        else
                        {
                            MergeOverlayGraphics(result.OverlayImage, CurrentImage!, compositeOverlay);
                        }
                    }
                }
                finally
                {
                    tool.ClearCachedGrayscale();
                    // Only dispose images we own (cloned base images).
                    // Connected images are shared references — not ours to dispose.
                    if (usesBaseImage)
                        inputImage.Dispose();
                }
            }

            // Dispose cached grayscale images
            baseGray?.Dispose();
            foreach (var g in connectedGrayCache.Values)
                g.Dispose();
            connectedGrayCache.Clear();

            LastCompositeOverlay = compositeOverlay;

            sw.Stop();
            TotalExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastRunSuccess = allSuccess;
            IsRunning = false;

            return results;
        }

        /// <summary>
        /// 도구 타입에 따른 새 인스턴스 생성
        /// </summary>
        public static VisionToolBase? CreateTool(string toolType)
        {
            return toolType switch
            {
                // Image Processing
                "GrayscaleTool" => new GrayscaleTool(),
                "BlurTool" => new BlurTool(),
                "ThresholdTool" => new ThresholdTool(),
                "EdgeDetectionTool" => new EdgeDetectionTool(),
                "MorphologyTool" => new MorphologyTool(),
                "HistogramTool" => new HistogramTool(),
                "HeightSlicerTool" => new HeightSlicerTool(),

                // Pattern Matching
                "FeatureMatchTool" => new FeatureMatchTool(),

                // Blob Analysis
                "BlobTool" => new BlobTool(),

                // Measurement
                "CaliperTool" => new CaliperTool(),
                "LineFitTool" => new LineFitTool(),
                "CircleFitTool" => new CircleFitTool(),

                _ => null
            };
        }

        /// <summary>
        /// 사용 가능한 모든 도구 타입 목록
        /// </summary>
        public static Dictionary<string, string[]> GetAvailableTools()
        {
            return new Dictionary<string, string[]>
            {
                ["Image Processing"] = new[]
                {
                    "GrayscaleTool",
                    "BlurTool",
                    "ThresholdTool",
                    "EdgeDetectionTool",
                    "MorphologyTool",
                    "HistogramTool"
                },
                ["3D Analysis"] = new[]
                {
                    "HeightSlicerTool"
                },
                ["Pattern Matching"] = new[]
                {
                    "FeatureMatchTool"
                },
                ["Blob Analysis"] = new[]
                {
                    "BlobTool"
                },
                ["Measurement"] = new[]
                {
                    "CaliperTool",
                    "LineFitTool",
                    "CircleFitTool"
                }
            };
        }

        /// <summary>
        /// 도구 타입에 따른 표시 이름
        /// </summary>
        public static string GetToolDisplayName(string toolType)
        {
            return toolType switch
            {
                "GrayscaleTool" => "Grayscale",
                "BlurTool" => "Blur",
                "ThresholdTool" => "Threshold",
                "EdgeDetectionTool" => "Edge Detection",
                "MorphologyTool" => "Morphology",
                "HistogramTool" => "Histogram",
                "FeatureMatchTool" => "Feature Match",
                "BlobTool" => "Blob Analysis",
                "CaliperTool" => "Caliper",
                "LineFitTool" => "Line Fit",
                "CircleFitTool" => "Circle Fit",
                "HeightSlicerTool" => "Height Slicer",
                _ => toolType
            };
        }

        /// <summary>
        /// 3D 포인트 클라우드를 높이 슬라이싱하여 2D 그레이스케일 Height Map 생성
        /// Y축 = 높이, 정렬된(organized) 그리드 포인트 클라우드 필요
        /// </summary>
        public (Mat HeightMap, HeightMapMetadata Metadata) GenerateHeightMap(
            PointCloudData pointCloud, float zRef, float zMin, float zMax)
        {
            if (!pointCloud.IsOrganized)
                throw new InvalidOperationException("Height map requires an organized (grid) point cloud.");

            int w = pointCloud.GridWidth;
            int h = pointCloud.GridHeight;
            float range = zMax - zMin;
            if (range <= 0) range = 1f;

            var heightMap = new Mat(h, w, MatType.CV_8UC1, Scalar.All(0));
            var pixelTo3D = new Vector3?[w * h];

            unsafe
            {
                byte* ptr = (byte*)heightMap.Data;
                var positions = pointCloud.Positions;

                for (int row = 0; row < h; row++)
                {
                    for (int col = 0; col < w; col++)
                    {
                        int idx = row * w + col;
                        var pos = positions[idx];
                        pixelTo3D[idx] = pos;

                        float normalizedY = pos.Y - zRef;
                        float t = (normalizedY - zMin) / range;
                        t = Math.Clamp(t, 0f, 1f);
                        ptr[idx] = (byte)(t * 255f);
                    }
                }
            }

            var metadata = new HeightMapMetadata
            {
                Width = w,
                Height = h,
                ZReference = zRef,
                ZMin = zMin,
                ZMax = zMax,
                PixelTo3D = pixelTo3D
            };

            return (heightMap, metadata);
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            CurrentImage?.Dispose();
            CurrentImage = null;
            LastCompositeOverlay = null;
        }
    }
}
