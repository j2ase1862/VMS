using VMS.Camera.Converters;
using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;
using VMS.VisionSetup.VisionTools.CodeReading;
using VMS.VisionSetup.VisionTools.Identification;
using VMS.VisionSetup.VisionTools.DeepLearning;
using VMS.VisionSetup.VisionTools.Result;
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

        // 마지막 실행에서 발생한 파이프라인 경고 (연결 사이클, 이미지 연결 폴백 등)
        // 실행 자체는 계속하되 사용자에게 표면화해야 하는 상황을 누적. null이면 경고 없음.
        private string? _lastPipelineWarning;
        public string? LastPipelineWarning
        {
            get => _lastPipelineWarning;
            private set => SetProperty(ref _lastPipelineWarning, value);
        }

        /// <summary>파이프라인 경고 누적 (실행 중 여러 건 발생 가능 — " / "로 연결)</summary>
        private void AppendPipelineWarning(string message)
        {
            Debug.WriteLine($"[VisionService] Pipeline warning: {message}");
            LastPipelineWarning = string.IsNullOrEmpty(LastPipelineWarning)
                ? message
                : $"{LastPipelineWarning} / {message}";
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

        // CV_32FC1 float depth map (HeightSlicerTool 등 정밀 도구용)
        private Mat? _currentDepthMap32F;
        public Mat? CurrentDepthMap32F
        {
            get => _currentDepthMap32F;
            private set
            {
                _currentDepthMap32F?.Dispose();
                SetProperty(ref _currentDepthMap32F, value);
            }
        }

        // 3D 높이맵 메타데이터 (PlaneFitTool, Geometry3DTool 등 3D 측정 도구용)
        private HeightMapMetadata? _currentHeightMapMetadata;
        public HeightMapMetadata? CurrentHeightMapMetadata
        {
            get => _currentHeightMapMetadata;
            set => SetProperty(ref _currentHeightMapMetadata, value);
        }

        // 카메라 캘리브레이션 메타데이터 (CalibrationTool이 set, ImageRectifyTool/측정 도구가 get)
        private CalibrationMetadata? _currentCalibrationMetadata;
        public CalibrationMetadata? CurrentCalibrationMetadata
        {
            get => _currentCalibrationMetadata;
            set => SetProperty(ref _currentCalibrationMetadata, value);
        }

        // ── 스텝 Resolution 폴백 (mm/px) ──
        // InspectionStep.Resolution 은 스텝 그리드에서 편집되는 수동 mm/px 값인데,
        // 그동안 어떤 측정도 이 값을 쓰지 않았다(죽은 속성). 캘리브레이션이 없을 때의
        // 폴백으로 연결한다 — MainViewModel 이 스텝 로드/실행 시점에 갱신.
        private double _currentStepResolutionMmPerPx;
        private CalibrationMetadata? _stepResolutionFallback;

        /// <summary>현재 워크스페이스가 편집 중인 스텝 Id — StepPoseStore 기록 키 (다중 스텝 얼라인).</summary>
        public string? CurrentStepId { get; set; }

        /// <summary>현재 워크스페이스 스텝의 Resolution (mm/px). 0 이면 폴백 없음.</summary>
        public double CurrentStepResolutionMmPerPx
        {
            get => _currentStepResolutionMmPerPx;
            set
            {
                if (SetProperty(ref _currentStepResolutionMmPerPx, Math.Max(0, value)))
                    _stepResolutionFallback = null;   // 값이 바뀌면 합성 메타데이터 재생성
            }
        }

        /// <summary>
        /// 측정 도구가 mm 변환에 쓰는 실효 캘리브레이션.
        /// 우선순위: 정식 캘리브레이션(CurrentCalibrationMetadata) &gt; 스텝 Resolution 폴백 &gt; 없음.
        /// 폴백은 등방 스케일(SinglePointScale)로 합성 — 정식 캘리브레이션을 수동 입력값이
        /// 덮지 않도록 정식 쪽이 항상 이긴다. 왜곡 보정(ImageRectifyTool)은 카메라 행렬이
        /// 필요하므로 이 폴백을 쓰지 않고 CurrentCalibrationMetadata 를 직접 본다.
        /// </summary>
        public CalibrationMetadata? EffectiveCalibration
        {
            get
            {
                if (_currentCalibrationMetadata != null) return _currentCalibrationMetadata;
                if (_currentStepResolutionMmPerPx <= 0) return null;
                return _stepResolutionFallback ??= new CalibrationMetadata
                {
                    Mode = CalibrationMode.SinglePointScale,
                    PixelSizeMm = _currentStepResolutionMmPerPx,
                    SourceToolName = "Step Resolution",
                };
            }
        }

        // 3D 파이프라인 재실행 지원 — PointCloud 도구는 CurrentPointCloud를 교체(소비)하므로
        // 재Grab 없이 재실행하면 직전 실행의 산출물 위에서 돌게 된다. ExecuteAll 시작 시
        // '직전 실행 산출물이 그대로'면 직전 실행의 입력 점군으로 복원한다.
        private VMS.Camera.Models.PointCloudData? _pipelineCloudInput;
        private VMS.Camera.Models.PointCloudData? _pipelineCloudOutput;

        /// <summary>
        /// 진행 중(또는 직전) 파이프라인 실행의 입력 점군.
        /// 병렬 브랜치 도구(PointCloudMaskCropTool Union 모드 등)가 앞 도구의 파괴적 갱신과
        /// 무관하게 '실행 시작 시점 점군'에서 자를 수 있도록 노출한다.
        /// </summary>
        public VMS.Camera.Models.PointCloudData? PipelineInputPointCloud => _pipelineCloudInput;

        // 3D 점군 데이터 (PointCloudFilterTool 등 점군 처리 도구가 get/set, MainViewModel과 동기화)
        private VMS.Camera.Models.PointCloudData? _currentPointCloud;
        public VMS.Camera.Models.PointCloudData? CurrentPointCloud
        {
            get => _currentPointCloud;
            set => SetProperty(ref _currentPointCloud, value);
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
        /// 대상 도구로 들어오는 지정 타입 연결의 소스 도구 목록 (연결 추가 순서 유지).
        /// 설정 UI가 연결된 소스(예: Geometry3D의 클러스터 소스)를 표시할 때 사용.
        /// </summary>
        public List<VisionToolBase> GetConnectedSources(VisionToolBase target, ConnectionType type)
        {
            var sources = new List<VisionToolBase>();
            foreach (var conn in _connections.Where(c => c.TargetId == target.Id && c.Type == type))
            {
                var src = Tools.FirstOrDefault(t => t.Id == conn.SourceId);
                if (src != null)
                    sources.Add(src);
            }
            return sources;
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

                // Source가 OutputImage를 만들지 못한 경우(도구 실행 실패 등) 원본 이미지로 폴백.
                // 사용자가 명시적으로 연결한 경로가 무시되는 것이므로 침묵하지 않고 경고로 표면화.
                var sourceName = Tools.FirstOrDefault(t => t.Id == imageConnection.SourceId)?.Name
                    ?? imageConnection.SourceId;
                AppendPipelineWarning(
                    $"'{sourceName}'의 출력 이미지가 없어 '{tool.Name}'에 원본 이미지가 입력됨");
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

            // Fixture Transform 중 ROI/UseROI 변경이 HasFixtureBaseROI를 리셋하지 않도록 플래그 설정
            tool.IsFixtureTransformActive = true;
            try
            {
                foreach (var conn in coordConnections)
                {
                    if (!resultMap.TryGetValue(conn.SourceId, out var sourceResult) || sourceResult.Data == null)
                        continue;

                    // PolarUnwrapTool 특수 처리 — CircleFit/SourceCenter를 그대로 Center/Radius로 주입
                    // (ROI fixture 변환과 다른 의미 — 회전/이동 보정이 아니라 원의 실제 위치 전달)
                    if (tool is VisionTools.ImageProcessing.PolarUnwrapTool polar
                        && sourceResult.Data.TryGetValue("CenterX", out var pcx)
                        && sourceResult.Data.TryGetValue("CenterY", out var pcy))
                    {
                        polar.CenterX = Convert.ToDouble(pcx);
                        polar.CenterY = Convert.ToDouble(pcy);
                        if (sourceResult.Data.TryGetValue("Radius", out var prObj))
                        {
                            double r = Convert.ToDouble(prObj);
                            // 사용자가 InnerRadius/OuterRadius를 명시 안 했으면 (둘 다 0) 합리적 기본값
                            if (polar.OuterRadius <= 0 || polar.InnerRadius < 0)
                            {
                                polar.InnerRadius = Math.Max(0, r * 0.6);
                                polar.OuterRadius = r * 1.1;
                            }
                        }
                        continue; // fixture 경로 스킵
                    }

                    // Fixture transform: CenterX/CenterY available → apply delta
                    if (sourceResult.Data.TryGetValue("CenterX", out var cx) &&
                        sourceResult.Data.TryGetValue("CenterY", out var cy))
                    {
                        // Save user-configured ROI and initial FeatureMatch result on first fixture application
                        if (!tool.HasFixtureBaseROI)
                        {
                            double refCX = Convert.ToDouble(cx);
                            double refCY = Convert.ToDouble(cy);

                            if (tool.UseROI && tool.ROI.Width > 0 && tool.ROI.Height > 0)
                            {
                                // User has drawn a ROI — use it as fixture base
                                tool.FixtureBaseROI = tool.ROI;
                            }
                            else
                            {
                                // No user-defined ROI — create a default fixture base centered on
                                // the reference pattern position so the ROI follows the pattern.
                                int defaultW = tool.ROI.Width > 0 ? tool.ROI.Width : 200;
                                int defaultH = tool.ROI.Height > 0 ? tool.ROI.Height : 200;
                                tool.FixtureBaseROI = new Rect(
                                    (int)(refCX - defaultW / 2.0),
                                    (int)(refCY - defaultH / 2.0),
                                    defaultW, defaultH);
                            }

                            tool.HasFixtureBaseROI = true;
                            tool.FixtureRefX = refCX;
                            tool.FixtureRefY = refCY;
                            tool.FixtureRefAngle = sourceResult.Data.TryGetValue("Angle", out var initAngle)
                                ? Convert.ToDouble(initAngle) : 0;
                        }

                        double foundX = Convert.ToDouble(cx);
                        double foundY = Convert.ToDouble(cy);
                        double refX = tool.FixtureRefX;
                        double refY = tool.FixtureRefY;

                        double baseCX = tool.FixtureBaseROI.X + tool.FixtureBaseROI.Width / 2.0;
                        double baseCY = tool.FixtureBaseROI.Y + tool.FixtureBaseROI.Height / 2.0;

                        double currentAngle = 0;
                        if (sourceResult.Data.TryGetValue("Angle", out var angleObj))
                            currentAngle = Convert.ToDouble(angleObj);
                        double deltaAngle = currentAngle - tool.FixtureRefAngle;

                        double newCX, newCY;
                        if (Math.Abs(deltaAngle) > 0.01)
                        {
                            // Rotate ROI center around reference center, then translate by delta
                            double relX = baseCX - refX;
                            double relY = baseCY - refY;
                            double rad = deltaAngle * Math.PI / 180.0;
                            newCX = foundX + relX * Math.Cos(rad) - relY * Math.Sin(rad);
                            newCY = foundY + relX * Math.Sin(rad) + relY * Math.Cos(rad);
                        }
                        else
                        {
                            // Translation only
                            newCX = baseCX + (foundX - refX);
                            newCY = baseCY + (foundY - refY);
                        }

                        int w = tool.FixtureBaseROI.Width > 0 ? tool.FixtureBaseROI.Width : 100;
                        int h = tool.FixtureBaseROI.Height > 0 ? tool.FixtureBaseROI.Height : 100;
                        tool.ROI = new Rect((int)(newCX - w / 2.0), (int)(newCY - h / 2.0), w, h);
                        tool.UseROI = true;

                        // SearchRegion(Execute용)도 같은 delta로 시프트.
                        // ShapeMatch/Color*/OCV처럼 Training Region과 별도 Search Region을 갖는 도구에 적용.
                        if (tool is ISearchRegionTool srt && srt.UseSearchRegion
                            && srt.SearchRegion.Width > 0 && srt.SearchRegion.Height > 0)
                        {
                            if (!tool.HasFixtureBaseSearchRegion)
                            {
                                tool.FixtureBaseSearchRegion = srt.SearchRegion;
                                tool.HasFixtureBaseSearchRegion = true;
                            }

                            double srBaseCX = tool.FixtureBaseSearchRegion.X + tool.FixtureBaseSearchRegion.Width / 2.0;
                            double srBaseCY = tool.FixtureBaseSearchRegion.Y + tool.FixtureBaseSearchRegion.Height / 2.0;

                            double newSrCX, newSrCY;
                            if (Math.Abs(deltaAngle) > 0.01)
                            {
                                double srRelX = srBaseCX - refX;
                                double srRelY = srBaseCY - refY;
                                double rad2 = deltaAngle * Math.PI / 180.0;
                                newSrCX = foundX + srRelX * Math.Cos(rad2) - srRelY * Math.Sin(rad2);
                                newSrCY = foundY + srRelX * Math.Sin(rad2) + srRelY * Math.Cos(rad2);
                            }
                            else
                            {
                                newSrCX = srBaseCX + (foundX - refX);
                                newSrCY = srBaseCY + (foundY - refY);
                            }

                            int sw = tool.FixtureBaseSearchRegion.Width;
                            int sh = tool.FixtureBaseSearchRegion.Height;
                            srt.SearchRegion = new Rect(
                                (int)(newSrCX - sw / 2.0), (int)(newSrCY - sh / 2.0), sw, sh);
                        }
                    }
                    // Fallback: BoundingRect (CenterX/CenterY가 없는 소스용)
                    else if (sourceResult.Data.TryGetValue("BoundingRect", out var rectObj) && rectObj is Rect boundingRect)
                    {
                        tool.ROI = boundingRect;
                        tool.UseROI = true;
                    }
                    // NOTE: 과거 여기 있던 "center-based (non-fixture sources)" 폴백 분기는 제거됨.
                    // 초기 구현에서는 첫 분기가 TrainedCenterX/Y(FeatureMatch 전용 키)를 요구해
                    // CenterX/CenterY만 가진 비-Fixture 소스(Blob 등)가 이 폴백으로 처리됐으나,
                    // 32e75f0에서 첫 분기가 "첫 적용 시점 스냅샷(FixtureRef*)" 방식으로 일반화되며
                    // CenterX/CenterY만 있어도 첫 분기가 처리하게 됨 (사용자 ROI 미지정 시
                    // FixtureBaseROI가 기준 좌표 중심으로 생성되어 폴백과 동일한 배치 결과).
                    // → 조건이 첫 분기와 동일해져 도달 불가한 죽은 코드였음.
                }
            }
            finally
            {
                tool.IsFixtureTransformActive = false;
            }
        }

        #endregion

        #region Topological Sort

        /// <summary>
        /// 연결 의존성에 따라 도구를 위상 정렬 (소스가 타겟보다 먼저 실행되도록)
        /// </summary>
        /// <param name="cycleTools">사이클에 포함되어 위상 정렬이 불가능했던 도구들 (원래 순서로 뒤에 append됨)</param>
        private List<VisionToolBase> TopologicalSort(IEnumerable<VisionToolBase> tools, out List<VisionToolBase> cycleTools)
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

            // If cycle detected (sorted.Count < toolList.Count), append remaining in original order.
            // 실행 자체는 계속하되 호출자(ExecuteAll)가 cycleTools로 경고를 표면화한다.
            cycleTools = new List<VisionToolBase>();
            if (sorted.Count < toolList.Count)
            {
                var sortedIds = new HashSet<string>(sorted.Select(t => t.Id));
                foreach (var t in toolList)
                {
                    if (!sortedIds.Contains(t.Id))
                    {
                        sorted.Add(t);
                        cycleTools.Add(t);
                    }
                }
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
            LastPipelineWarning = null;

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

            // 3D 파이프라인 복원 — ExecuteAll과 동일 규칙:
            // 새 Grab/로드 없이 재실행하면 직전 실행의 입력 점군으로 되돌린 뒤 시작한다.
            // (Run Selected를 반복해도 점군이 계속 좁아지지 않도록)
            if (_pipelineCloudInput != null
                && ReferenceEquals(CurrentPointCloud, _pipelineCloudOutput)
                && !ReferenceEquals(_pipelineCloudInput, _pipelineCloudOutput))
            {
                CurrentPointCloud = _pipelineCloudInput;
            }
            _pipelineCloudInput = CurrentPointCloud;

            // Run upstream dependencies first so connected input is available
            var upstream = GetUpstreamDependencies(tool);
            var resultMap = new Dictionary<string, VisionResult>();

            var clonedInputs = new List<Mat>();

            foreach (var dep in upstream)
            {
                if (!dep.IsEnabled) continue;

                // Apply coordinates connection for upstream dependencies too
                ApplyCoordinatesConnection(dep, resultMap);

                var depConnected = GetConnectedInputImage(dep, resultMap);
                Mat depInput;
                bool ownsInput;
                if (depConnected != null)
                {
                    depInput = depConnected;
                    ownsInput = false;
                }
                else
                {
                    depInput = image.Clone();
                    ownsInput = true;
                }

                try
                {
                    var depResult = dep.Execute(depInput);
                    dep.LastResult = depResult;
                    resultMap[dep.Id] = depResult;
                    StepPoseStore.Record(CurrentStepId, dep, depResult);
                }
                finally
                {
                    // Only dispose images we own (cloned base images).
                    // Connected images are shared references — not ours to dispose.
                    if (ownsInput)
                        clonedInputs.Add(depInput);
                }
            }

            // Apply coordinates connection: shift ROI based on source tool's result
            ApplyCoordinatesConnection(tool, resultMap);

            // Resolve connected input for the target tool
            // HeightSlicerTool: 연결이 없을 때 CV_32FC1 depth map 입력
            var connectedImage = GetConnectedInputImage(tool, resultMap);
            Mat toolInput;
            bool ownsToolInput;
            if (connectedImage != null)
            {
                toolInput = connectedImage;
                ownsToolInput = false;
            }
            else if (tool is HeightSlicerTool && CurrentDepthMap32F != null && !CurrentDepthMap32F.IsDisposed)
            {
                toolInput = CurrentDepthMap32F.Clone();
                ownsToolInput = true;
            }
            else
            {
                toolInput = image.Clone();
                ownsToolInput = true;
            }

            try
            {
                // Run Selected에서도 3D 기하 소스 주입 (업스트림 결과는 resultMap에 수집됨)
                if (tool is Geometry3DTool g3)
                    InjectGeometry3DSources(g3, resultMap, Tools);

                // Run Selected에서도 매칭 소스 주입 (업스트림 실행 결과 사용)
                if (tool is MatchAlignTool mat)
                    InjectMatchAlignSource(mat, resultMap);

                tool.OverlayBaseImage = toolInput;
                var result = tool.Execute(toolInput);
                tool.LastResult = result;
                StepPoseStore.Record(CurrentStepId, tool, result);
                _pipelineCloudOutput = CurrentPointCloud;
                return result;
            }
            finally
            {
                tool.OverlayBaseImage = null;
                if (ownsToolInput)
                    toolInput.Dispose();
                foreach (var ci in clonedInputs)
                    ci.Dispose();
            }
        }

        /// <summary>
        /// MatchAlignTool의 Result 연결 소스에서 매칭 포즈(CenterX/Y 보유 결과)를 주입 —
        /// 연결 순서대로 1번/2번 포인트 (공용 로직은 ToolSourceInjector, VMS 메인과 공유).
        /// </summary>
        private void InjectMatchAlignSource(MatchAlignTool mat, Dictionary<string, VisionResult> resultMap)
        {
            ToolSourceInjector.InjectMatchAlign(mat, EnumerateResultSources(mat.Id, resultMap));
        }

        /// <summary>대상 도구를 향한 Result 연결 소스를 연결 생성 순서대로 열거.</summary>
        private IEnumerable<ToolSourceInjector.ResultSource> EnumerateResultSources(
            string targetId, Dictionary<string, VisionResult> resultMap)
        {
            foreach (var conn in _connections
                .Where(c => c.TargetId == targetId && c.Type == ConnectionType.Result))
            {
                if (resultMap.TryGetValue(conn.SourceId, out var srcResult))
                {
                    var srcTool = Tools.FirstOrDefault(t => t.Id == conn.SourceId);
                    yield return new ToolSourceInjector.ResultSource(
                        conn.SourceId, srcResult,
                        srcTool?.Name ?? conn.SourceId, srcTool?.ToolType ?? string.Empty);
                }
            }
        }

        /// <summary>
        /// Geometry3DTool의 Result 연결 소스에서 3D 기하 요소(평면/클러스터 중심점)를 수집.
        /// 클러스터 소스가 하나뿐이면 FinalizeSourceGeometries가 SourceB 번호의 중심점을
        /// 추가해 클러스터 툴 1개로도 두 객체 간 거리 측정이 가능하다.
        /// </summary>
        private void InjectGeometry3DSources(Geometry3DTool g3,
            Dictionary<string, VisionResult> resultMap, IEnumerable<VisionToolBase> tools)
        {
            g3.ClearSourceGeometries();
            foreach (var conn in _connections
                .Where(c => c.TargetId == g3.Id && c.Type == ConnectionType.Result))
            {
                if (resultMap.TryGetValue(conn.SourceId, out var srcResult))
                {
                    var srcTool = tools.FirstOrDefault(t => t.Id == conn.SourceId);
                    g3.CollectSourceGeometry(
                        conn.SourceId,
                        srcTool?.Name ?? conn.SourceId,
                        srcTool?.ToolType ?? string.Empty,
                        srcResult);
                }
            }
            g3.FinalizeSourceGeometries();
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
            // 이전 실행 결과의 Mat을 먼저 해제 — 연속 검사(AUTO RUN)에서
            // 참조만 끊긴 대형 네이티브 버퍼가 GC 파이널라이저에 의존해 쌓이는 것을 방지
            ReleasePreviousResults();
            LastPipelineWarning = null;

            // Results(ObservableCollection)는 UI 스레드 소유 — ExecuteAllAsync가 Task.Run으로
            // 이 메서드를 백그라운드에서 실행하므로 여기서는 로컬 리스트에만 모으고,
            // 실행 완료 후 PublishResults()가 UI 스레드로 마샬링해 일괄 반영한다.
            var results = new List<VisionResult>();

            if (CurrentImage == null || CurrentImage.Empty())
            {
                var errorResult = new VisionResult
                {
                    Success = false,
                    Message = "입력 이미지가 없습니다."
                };
                results.Add(errorResult);
                PublishResults(results);
                LastRunSuccess = false;
                return results;
            }

            IsRunning = true;
            var sw = Stopwatch.StartNew();

            // 새 Grab/로드 없이 재실행: 직전 실행의 입력 점군으로 복원 (파괴적 갱신 보정)
            if (_pipelineCloudInput != null
                && ReferenceEquals(CurrentPointCloud, _pipelineCloudOutput)
                && !ReferenceEquals(_pipelineCloudInput, _pipelineCloudOutput))
            {
                CurrentPointCloud = _pipelineCloudInput;
            }
            _pipelineCloudInput = CurrentPointCloud;

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
            var sortedTools = TopologicalSort(Tools, out var cycleTools);
            if (cycleTools.Count > 0)
            {
                // 사이클이 있으면 해당 도구들은 의존성 보장 없이 원래 순서로 실행됨 — 침묵하지 않고 표면화
                AppendPipelineWarning(
                    $"도구 연결에 순환이 감지되어 다음 도구는 연결 순서 보장 없이 실행됨: {string.Join(", ", cycleTools.Select(t => t.Name))}");
            }

            foreach (var tool in sortedTools)
            {
                if (!tool.IsEnabled)
                    continue;

                // 1. Result 연결 확인: Source가 실패이면 건너뛰기
                //    ResultTool / EnsembleTool은 실패 정보를 수집해야 하므로 스킵 우회
                if (tool is not ResultTool and not GeometryTool and not Geometry3DTool and not EnsembleTool && ShouldSkipByResultConnection(tool, resultMap))
                {
                    var skipResult = new VisionResult
                    {
                        Success = false,
                        Message = $"연결된 도구의 결과가 실패하여 건너뜀: {tool.Name}"
                    };
                    results.Add(skipResult);
                    resultMap[tool.Id] = skipResult;
                    allSuccess = false;
                    continue;
                }

                // 2. Coordinates 연결: Source의 좌표 데이터를 현재 도구에 적용
                ApplyCoordinatesConnection(tool, resultMap);

                // 3. Image 연결: 연결된 Source의 출력 이미지를 입력으로 사용
                //    연결이 없으면 원본 이미지 사용 (각 도구가 독립적으로 원본 처리)
                //    HeightSlicerTool은 연결이 없을 때 CV_32FC1 depth map을 입력으로 수신
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
                else if (tool is HeightSlicerTool && CurrentDepthMap32F != null && !CurrentDepthMap32F.IsDisposed)
                {
                    // HeightSlicerTool: CV_32FC1 float depth map 입력
                    inputImage = CurrentDepthMap32F.Clone();
                }
                else
                {
                    inputImage = CurrentImage.Clone();
                    if (baseGray != null)
                        tool.SetCachedGrayscale(baseGray.Clone());
                }

                try
                {
                    // ResultTool: Execute 전에 연결된 소스 결과 주입
                    if (tool is ResultTool rt)
                    {
                        rt.SourceResults.Clear();
                        foreach (var conn in _connections
                            .Where(c => c.TargetId == tool.Id && c.Type == ConnectionType.Result))
                        {
                            if (resultMap.TryGetValue(conn.SourceId, out var srcResult))
                            {
                                var srcTool = sortedTools.FirstOrDefault(t => t.Id == conn.SourceId);
                                rt.SourceResults.Add(new SourceToolResult
                                {
                                    ToolId = conn.SourceId,
                                    ToolName = srcTool?.Name ?? conn.SourceId,
                                    Success = srcResult.Success,
                                    Message = srcResult.Message
                                });
                            }
                        }
                    }

                    // EnsembleTool: Execute 전에 연결된 소스의 전체 VisionResult 주입
                    if (tool is EnsembleTool et)
                    {
                        et.SourceResults.Clear();
                        foreach (var conn in _connections
                            .Where(c => c.TargetId == tool.Id && c.Type == ConnectionType.Result))
                        {
                            if (resultMap.TryGetValue(conn.SourceId, out var srcResult))
                            {
                                var srcTool = sortedTools.FirstOrDefault(t => t.Id == conn.SourceId);
                                et.SourceResults.Add(new SourceToolResultEx
                                {
                                    ToolId = conn.SourceId,
                                    ToolName = srcTool?.Name ?? conn.SourceId,
                                    ToolType = srcTool?.ToolType ?? string.Empty,
                                    Success = srcResult.Success,
                                    Message = srcResult.Message,
                                    FullResult = srcResult
                                });
                            }
                        }
                    }

                    // GeometryTool: Execute 전에 연결된 소스의 기하 데이터 주입
                    if (tool is GeometryTool gt)
                    {
                        gt.SourceGeometries.Clear();
                        foreach (var conn in _connections
                            .Where(c => c.TargetId == tool.Id && c.Type == ConnectionType.Result))
                        {
                            if (resultMap.TryGetValue(conn.SourceId, out var srcResult))
                            {
                                var srcTool = sortedTools.FirstOrDefault(t => t.Id == conn.SourceId);
                                var geo = GeometryTool.ExtractGeometry(
                                    conn.SourceId,
                                    srcTool?.Name ?? conn.SourceId,
                                    srcTool?.ToolType ?? "",
                                    srcResult);
                                if (geo != null)
                                    gt.SourceGeometries.Add(geo);
                            }
                        }
                    }

                    // Geometry3DTool: Execute 전에 연결된 소스의 3D 기하 요소 주입
                    // (PlaneFitTool → 평면, PointCloudClusterTool → 클러스터 중심점 mm)
                    if (tool is Geometry3DTool g3)
                        InjectGeometry3DSources(g3, resultMap, sortedTools);

                    // MatchAlignTool: Execute 전에 연결된 매칭 소스(CenterX/Y/Angle) 주입
                    if (tool is MatchAlignTool mat)
                        InjectMatchAlignSource(mat, resultMap);

                    var result = tool.Execute(inputImage);
                    tool.LastResult = result;
                    results.Add(result);
                    resultMap[tool.Id] = result;

                    // 다중 스텝 얼라인용 포즈 기록 (CenterX/Y 보유 결과만)
                    StepPoseStore.Record(CurrentStepId, tool, result);

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

            // 실행 결과를 UI 스레드에서 일괄 반영 (백그라운드 CollectionChanged 크래시 방지)
            PublishResults(results);

            sw.Stop();
            TotalExecutionTime = sw.Elapsed.TotalMilliseconds;

            // 외부에서 도구↔결과 매핑이 필요한 경우 사용 (BatchTestRunner 등)
            // resultMap은 topological sort 영향 없이 id로 안전하게 lookup 가능
            LastExecutionResultsById = new Dictionary<string, VisionResult>(resultMap);

            // ResultTool이 존재하면 최종 판정은 ResultTool의 Success로 결정
            var resultToolInstance = sortedTools.OfType<ResultTool>().FirstOrDefault();
            LastRunSuccess = resultToolInstance != null
                ? resultMap.TryGetValue(resultToolInstance.Id, out var rtResult) && rtResult.Success
                : allSuccess;
            _pipelineCloudOutput = CurrentPointCloud;
            IsRunning = false;

            return results;
        }

        /// <summary>
        /// 마지막 ExecuteAll 실행의 도구 ID → 결과 매핑.
        /// ExecuteAll이 topological sort 순으로 결과를 반환하기 때문에,
        /// 외부에서 Tools 컬렉션 순서와 일치한다고 가정하면 안 됨. 이 dictionary로 안전 lookup.
        /// </summary>
        public Dictionary<string, VisionResult> LastExecutionResultsById { get; private set; } = new();

        /// <summary>
        /// 이전 실행 결과가 보유한 Mat(OutputImage/OverlayImage) 해제.
        /// Results / LastExecutionResultsById / 각 도구의 LastResult가 동일 VisionResult 인스턴스를
        /// 공유하므로 참조 기준으로 1회씩만 순회 (ReleaseMats 자체도 idempotent라 이중 해제 없음).
        /// 표시 경로(MainViewModel)는 실행 직후 ToWriteableBitmap()/Clone() 복사본을 만들고,
        /// CurrentImage/DisplayImage는 결과 Mat과 소유권이 분리되어 있으므로
        /// 다음 실행 시점의 이전 결과 해제는 화면에 표시 중인 이미지에 영향을 주지 않는다.
        /// </summary>
        private void ReleasePreviousResults()
        {
            var released = new HashSet<VisionResult>();
            foreach (var result in Results)
            {
                if (released.Add(result))
                    result.ReleaseMats();
            }
            foreach (var result in LastExecutionResultsById.Values)
            {
                if (released.Add(result))
                    result.ReleaseMats();
            }
        }

        /// <summary>
        /// 실행 결과를 Results(ObservableCollection)에 일괄 반영.
        /// ObservableCollection의 CollectionChanged는 컬렉션을 생성한(바인딩하는) 스레드에서만
        /// 안전하므로, 백그라운드 실행(ExecuteAllAsync → Task.Run) 중에는 로컬 리스트에 모았다가
        /// 이 메서드가 UI 디스패처로 마샬링해 Clear+Add를 한 번에 수행한다.
        /// Invoke(동기)를 사용해 ExecuteAll() 반환 시점에 Results가 최신임을 보장 (기존 호출자 동작 보존).
        /// </summary>
        private void PublishResults(List<VisionResult> results)
        {
            InvokeOnUIThread(() =>
            {
                Results.Clear();
                foreach (var r in results)
                    Results.Add(r);
            });
        }

        /// <summary>
        /// UI 디스패처에서 동기 실행. WPF Application이 없거나(단위 테스트)
        /// 이미 UI 스레드면 직접 실행.
        /// </summary>
        private static void InvokeOnUIThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
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
                "PlaneFitTool" => new PlaneFitTool(),
                "Geometry3DTool" => new Geometry3DTool(),
                "PointCloudFilterTool" => new VisionTools.PointCloud.PointCloudFilterTool(),
                "PointCloudRegistrationTool" => new VisionTools.PointCloud.PointCloudRegistrationTool(),
                "PointCloudClusterTool" => new VisionTools.PointCloud.PointCloudClusterTool(),
                "PointCloudDeviationTool" => new VisionTools.PointCloud.PointCloudDeviationTool(),
                "PointCloudMaskCropTool" => new VisionTools.PointCloud.PointCloudMaskCropTool(),

                // Pattern Matching
                "FeatureMatchTool" => new FeatureMatchTool(),
                "ShapeMatchTool" => new ShapeMatchTool(),
                "MatchAlignTool" => new MatchAlignTool(),
                "MultiStepAlignTool" => new MultiStepAlignTool(),

                // Blob Analysis
                "BlobTool" => new BlobTool(),

                // Measurement
                "CaliperTool" => new CaliperTool(),
                "LineFitTool" => new LineFitTool(),
                "CircleFitTool" => new CircleFitTool(),
                "GeometryTool" => new GeometryTool(),

                // Image Enhancement / Polar
                "ImageEnhanceTool" => new VisionTools.ImageProcessing.ImageEnhanceTool(),
                "PolarUnwrapTool" => new VisionTools.ImageProcessing.PolarUnwrapTool(),

                // Identification
                "OCRTool" => new OCRTool(),
                "OCVTool" => new OCVTool(),

                // Code Reading
                "CodeReaderTool" => new CodeReaderTool(),

                // Deep Learning
                "DetectionTool" => new DetectionTool(),
                "ClassifyTool" => new ClassifyTool(),
                "AnomalyTool" => new AnomalyTool(),
                "EnsembleTool" => new EnsembleTool(),
                "SegmentationTool" => new SegmentationTool(),
                "YoloSegTool" => new YoloSegTool(),
                "RfdetrSegTool" => new RfdetrSegTool(),

                // Judgment
                "ResultTool" => new ResultTool(),

                // Calibration
                "ImageRectifyTool" => new VisionTools.Calibration.ImageRectifyTool(),

                // Color
                "ColorExtractTool" => new VisionTools.Color.ColorExtractTool(),
                "ColorMatchTool" => new VisionTools.Color.ColorMatchTool(),

                // Surface Analysis
                "PhotometricStereoTool" => new VisionTools.SurfaceAnalysis.PhotometricStereoTool(),

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
                    "HistogramTool",
                    "ImageEnhanceTool",
                    "PolarUnwrapTool"
                },
                ["3D Analysis"] = new[]
                {
                    "PointCloudFilterTool",
                    "PointCloudRegistrationTool",
                    "PointCloudClusterTool",
                    "PointCloudMaskCropTool",
                    "HeightSlicerTool",
                    "PlaneFitTool",
                    "Geometry3DTool"
                },
                ["Pattern Matching"] = new[]
                {
                    "FeatureMatchTool",
                    "ShapeMatchTool",
                    "MatchAlignTool",
                    "MultiStepAlignTool"
                },
                ["Blob Analysis"] = new[]
                {
                    "BlobTool"
                },
                ["Measurement"] = new[]
                {
                    "CaliperTool",
                    "LineFitTool",
                    "CircleFitTool",
                    "GeometryTool"
                },
                ["Identification"] = new[]
                {
                    "OCRTool",
                    "OCVTool"
                },
                ["Code Reading"] = new[]
                {
                    "CodeReaderTool"
                },
                ["Deep Learning"] = new[]
                {
                    "DetectionTool",
                    "ClassifyTool",
                    "AnomalyTool",
                    "EnsembleTool",
                    "SegmentationTool",
                    "YoloSegTool",
                    "RfdetrSegTool"
                },
                ["Judgment"] = new[]
                {
                    "ResultTool"
                },
                ["Calibration"] = new[]
                {
                    "ImageRectifyTool"
                },
                ["Color"] = new[]
                {
                    "ColorExtractTool",
                    "ColorMatchTool"
                },
                ["Surface Analysis"] = new[]
                {
                    "PhotometricStereoTool"
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
                "ShapeMatchTool" => "Shape Match",
                "MatchAlignTool" => "Match Align",
                "MultiStepAlignTool" => "Multi-Step Align",
                "BlobTool" => "Blob Analysis",
                "CaliperTool" => "Caliper",
                "LineFitTool" => "Line Fit",
                "CircleFitTool" => "Circle Fit",
                "GeometryTool" => "Geometry",
                "HeightSlicerTool" => "Height Slicer",
                "PlaneFitTool" => "Plane Fit",
                "Geometry3DTool" => "3D Geometry",
                "PointCloudFilterTool" => "PointCloud Filter",
                "PointCloudRegistrationTool" => "PointCloud Registration",
                "PointCloudClusterTool" => "PointCloud Cluster",
                "PointCloudMaskCropTool" => "PointCloud Mask Crop",
                "OCRTool" => "OCR",
                "OCVTool" => "OCV",
                "ImageEnhanceTool" => "Image Enhance",
                "PolarUnwrapTool" => "Polar Unwrap",
                "CodeReaderTool" => "Code Reader",
                "DetectionTool" => "Detection (YOLO)",
                "ClassifyTool" => "Classify",
                "AnomalyTool" => "Anomaly",
                "EnsembleTool" => "Ensemble",
                "SegmentationTool" => "Segmentation",
                "YoloSegTool" => "YOLOv8-seg",
                "RfdetrSegTool" => "RF-DETR-seg",
                "ResultTool" => "Result",
                "ImageRectifyTool" => "Image Rectify",
                "ColorExtractTool" => "Color Extract",
                "ColorMatchTool" => "Color Match",
                _ => toolType
            };
        }

        /// <summary>
        /// 3D 포인트 클라우드를 높이 슬라이싱하여 2D Height Map 생성
        /// Z축 = 높이 (PointCloudViewer/DepthMapViewer와 일치)
        /// CV_32FC1 float map + CV_8UC1 정규화 map 동시 생성
        /// </summary>
        public (Mat HeightMap8U, Mat DepthMap32F, HeightMapMetadata Metadata) GenerateHeightMap(
            PointCloudData pointCloud, float zRef, float zMin, float zMax)
        {
            Mat depthMap32F;
            Vector3?[] pixelTo3D;
            int w, h;

            if (pointCloud.IsOrganized)
            {
                // 격자 점군 — 픽셀 1:1 매핑 (기존 고속 경로)
                depthMap32F = PointCloudConverter.ToDepthMap32F(pointCloud, zRef);
                w = pointCloud.GridWidth;
                h = pointCloud.GridHeight;
                pixelTo3D = new Vector3?[w * h];
                var positions = pointCloud.Positions;
                for (int i = 0; i < w * h; i++)
                {
                    pixelTo3D[i] = positions[i];
                }
            }
            else
            {
                // 비격자 점군(Registration/Cluster 후 등) — 정사투영 비닝
                (depthMap32F, pixelTo3D, w, h) =
                    PointCloudConverter.OrthographicToDepthMap32F(pointCloud, zRef);
            }

            var heightMap8U = PointCloudConverter.DepthMap32FTo8U(depthMap32F, zMin, zMax);

            var metadata = new HeightMapMetadata
            {
                Width = w,
                Height = h,
                ZReference = zRef,
                ZMin = zMin,
                ZMax = zMax,
                DepthMap32F = depthMap32F,
                PixelTo3D = pixelTo3D
            };

            // VisionService에 float map 보관 (HeightSlicerTool 라우팅용)
            CurrentDepthMap32F = depthMap32F.Clone();

            // 3D 측정 도구용 메타데이터 보관 (PlaneFitTool, Geometry3DTool)
            CurrentHeightMapMetadata = metadata;

            return (heightMap8U, depthMap32F, metadata);
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            // 마지막 실행 결과의 Mat도 함께 정리 (CurrentImage 등과 동일한 수명 종료 지점)
            ReleasePreviousResults();
            Results.Clear();
            LastExecutionResultsById = new Dictionary<string, VisionResult>();

            CurrentImage?.Dispose();
            CurrentImage = null;
            CurrentDepthMap32F = null;
            CurrentHeightMapMetadata = null;
            LastCompositeOverlay = null;
        }
    }
}
