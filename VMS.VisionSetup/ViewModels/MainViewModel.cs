using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.Services.Acquisition;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Windows.Media;
using CvRect = OpenCvSharp.Rect;

namespace VMS.VisionSetup.ViewModels
{
    public enum ImageDisplayMode
    {
        OriginalImage,
        ResultImage
    }

    #region Messenger Messages

    public class RequestShowToolROIMessage
    {
        public ROIShape? ROIShape { get; }
        public RequestShowToolROIMessage(ROIShape? roiShape) => ROIShape = roiShape;
    }

    public class RequestRefreshROIMessage
    {
        public ROIShape? ROIShape { get; }
        public RequestRefreshROIMessage(ROIShape? roiShape) => ROIShape = roiShape;
    }

    #endregion

    public partial class MainViewModel : ObservableObject
    {
        #region Fields
        private readonly string _appName = "BODA VISION AI";
        private readonly string _appVersion = "1.0.0";
        private readonly IVisionService _visionService;
        private readonly IRecipeService _recipeService;
        private readonly ICameraService _cameraService;
        private readonly IDialogService _dialogService;
        private readonly Action _shutdownAction;
        private Mat? _currentImage;
        private VisionToolBase? _subscribedTool;
        private bool _isSyncingROI;
        private ICameraAcquisition? _cameraAcquisition;
        #endregion

        #region Properties
        public string AppName => _appName;
        public string AppVersion => _appVersion;

        // 도구 트리 (사이드바)
        public ObservableCollection<ToolCategory> ToolTree { get; } = new();

        // 워크스페이스에 배치된 도구들
        public ObservableCollection<ToolItem> DroppedTools { get; } = new();

        // 실행 큐 (순서대로 실행될 도구들)
        public ObservableCollection<VisionToolBase> ExecutionQueue => _visionService.Tools;

        // 실행 결과
        public ObservableCollection<VisionResult> Results => _visionService.Results;

        // 도구 간 연결선 목록
        public ObservableCollection<ToolConnection> Connections { get; } = new();

        // 도구 실행 결과 목록 (DataGrid 바인딩용)
        public ObservableCollection<ToolResultItem> ToolRunResults { get; } = new();

        // 현재 이미지
        public Mat? CurrentImage
        {
            get => _currentImage;
            set
            {
                _currentImage?.Dispose();
                SetProperty(ref _currentImage, value);
                if (value != null)
                    _visionService.SetImage(value);
                UpdateDisplayImage();
                NotifyCommandsCanExecuteChanged();
            }
        }

        // 표시용 이미지
        [ObservableProperty]
        private ImageSource? _displayImage;

        // 오버레이 이미지
        [ObservableProperty]
        private ImageSource? _overlayImage;

        // 결과 이미지
        [ObservableProperty]
        private ImageSource? _resultImage;

        // 결과 Mat (ImageCanvas용)
        private Mat? _resultMat;
        public Mat? ResultMat
        {
            get => _resultMat;
            set
            {
                _resultMat?.Dispose();
                SetProperty(ref _resultMat, value);
                OnPropertyChanged(nameof(DisplayMat));
            }
        }

        // ImageCanvas용 원본 Mat
        public Mat? SourceMat => CurrentImage;

        // ROI 컬렉션
        private ObservableCollection<ROIShape>? _roiShapes;
        public ObservableCollection<ROIShape>? ROIShapes
        {
            get => _roiShapes ??= new ObservableCollection<ROIShape>();
            set => SetProperty(ref _roiShapes, value);
        }

        // 디스플레이 모드
        public ImageDisplayMode[] DisplayModes { get; } = (ImageDisplayMode[])Enum.GetValues(typeof(ImageDisplayMode));

        private ImageDisplayMode _selectedDisplayMode = ImageDisplayMode.OriginalImage;
        public ImageDisplayMode SelectedDisplayMode
        {
            get => _selectedDisplayMode;
            set
            {
                if (SetProperty(ref _selectedDisplayMode, value))
                {
                    OnPropertyChanged(nameof(DisplayMat));
                    OnPropertyChanged(nameof(IsOriginalImageMode));
                }
            }
        }

        public Mat? DisplayMat => _selectedDisplayMode == ImageDisplayMode.OriginalImage ? CurrentImage : _resultMat;

        public bool IsOriginalImageMode => _selectedDisplayMode == ImageDisplayMode.OriginalImage;

        // 선택된 ROI
        private ROIShape? _selectedROI;
        public ROIShape? SelectedROI
        {
            get => _selectedROI;
            set
            {
                SetProperty(ref _selectedROI, value);
                // 선택된 ROI를 현재 도구에 적용
                ApplyROIToSelectedTool();
            }
        }

        // 선택된 도구
        private ToolItem? _selectedTool;
        public ToolItem? SelectedTool
        {
            get => _selectedTool;
            set
            {
                // 이전 도구 PropertyChanged 구독 해제
                if (_subscribedTool != null)
                {
                    _subscribedTool.PropertyChanged -= OnSelectedToolPropertyChanged;
                    _subscribedTool = null;
                }

                // 이전 VM 정리
                _selectedToolSettings?.Dispose();

                SetProperty(ref _selectedTool, value);
                OnPropertyChanged(nameof(SelectedVisionTool));
                NotifyCommandsCanExecuteChanged();

                // 새 도구 PropertyChanged 구독 + VM 생성
                if (value?.VisionTool != null)
                {
                    _subscribedTool = value.VisionTool;
                    _subscribedTool.PropertyChanged += OnSelectedToolPropertyChanged;
                    SelectedToolSettings = CreateToolSettingsViewModel(value.VisionTool);
                }
                else
                {
                    SelectedToolSettings = null;
                }

                // 도구 전환 시 해당 도구의 ROI를 캔버스에 표시
                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(value?.VisionTool?.AssociatedROIShape));
            }
        }

        // 선택된 비전 도구 (설정 패널용)
        public VisionToolBase? SelectedVisionTool => SelectedTool?.VisionTool;

        // 선택된 도구의 ViewModel (ToolSettingsView DataContext)
        private ToolSettingsViewModelBase? _selectedToolSettings;
        public ToolSettingsViewModelBase? SelectedToolSettings
        {
            get => _selectedToolSettings;
            private set => SetProperty(ref _selectedToolSettings, value);
        }

        // 실행 중 여부
        [ObservableProperty]
        private bool _isRunning;

        // 상태 메시지
        [ObservableProperty]
        private string _statusMessage = "Ready";

        // 실행 시간
        [ObservableProperty]
        private string _executionTimeText = "";

        // 3D Point Cloud 데이터
        [ObservableProperty]
        private PointCloudData? _currentPointCloud;

        // Height Slicing 파라미터
        [ObservableProperty]
        private float _heightBaseline;

        [ObservableProperty]
        private float _heightLowerLimit = -60f;

        [ObservableProperty]
        private float _heightUpperLimit = 60f;

        [ObservableProperty]
        private float _pointCloudYMin = -60f;

        [ObservableProperty]
        private float _pointCloudYMax = 60f;

        public HeightMapMetadata? CurrentHeightMapMetadata { get; private set; }

        #region Camera Connection Properties

        // 카메라 정보 팝업 열림 상태
        [ObservableProperty]
        private bool _isCameraInfoPopupOpen;

        // 카메라 연결 상태
        [ObservableProperty]
        private bool _isCameraConnected;

        // 이미지 획득 중 상태
        [ObservableProperty]
        private bool _isAcquiring;

        #endregion

        #region Recipe / Camera / Step Properties

        // 현재 레시피 이름 표시
        [ObservableProperty]
        private string _currentRecipeName = "No Recipe";

        // 카메라 목록
        public ObservableCollection<CameraInfo> Cameras { get; } = new();

        // 선택된 카메라
        private CameraInfo? _selectedCamera;
        public CameraInfo? SelectedCamera
        {
            get => _selectedCamera;
            set
            {
                var previousCamera = _selectedCamera;
                if (SetProperty(ref _selectedCamera, value))
                {
                    // 카메라 변경 시 기존 연결 해제
                    if (previousCamera != null && IsCameraConnected)
                    {
                        _ = DisconnectCamera();
                    }

                    RefreshSteps();
                    AddStepCommand.NotifyCanExecuteChanged();
                    AcquireImageCommand.NotifyCanExecuteChanged();
                    ConnectCameraCommand.NotifyCanExecuteChanged();
                }
            }
        }

        // 스텝 목록 (선택된 카메라 기준)
        public ObservableCollection<InspectionStep> Steps { get; } = new();

        // 선택된 스텝
        private InspectionStep? _selectedStep;
        public InspectionStep? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (SetProperty(ref _selectedStep, value))
                {
                    if (value != null)
                        LoadStepToWorkspace(value);
                }
            }
        }

        #endregion

        #endregion

        #region Commands
        public RelayCommand CloseCommand { get; }
        public RelayCommand OpenImageFileCommand { get; }
        public RelayCommand RunAllCommand { get; }
        public RelayCommand RunSelectedCommand { get; }
        public RelayCommand ClearToolsCommand { get; }
        public RelayCommand<ToolItem> RemoveToolCommand { get; }
        public RelayCommand<ToolItem> MoveToolUpCommand { get; }
        public RelayCommand<ToolItem> MoveToolDownCommand { get; }
        public RelayCommand TrainPatternCommand { get; }
        public RelayCommand AutoTuneCommand { get; }
        public RelayCommand ClearResultImageCommand { get; }
        public RelayCommand AddStepCommand { get; }
        public RelayCommand DeleteStepCommand { get; }
        public RelayCommand MoveStepUpCommand { get; }
        public RelayCommand MoveStepDownCommand { get; }
        public RelayCommand LoadSamplePointCloudCommand { get; }
        public RelayCommand ShowCameraInfoCommand { get; }
        public RelayCommand AcquireImageCommand { get; }
        public RelayCommand ConnectCameraCommand { get; }
        public RelayCommand DisconnectCameraCommand { get; }
        public RelayCommand GenerateHeightMapCommand { get; }
        #endregion

        #region Constructor
        public MainViewModel(
            IVisionService visionService,
            IRecipeService recipeService,
            ICameraService cameraService,
            IDialogService dialogService,
            Action shutdownAction)
        {
            _visionService = visionService;
            _recipeService = recipeService;
            _cameraService = cameraService;
            _dialogService = dialogService;
            _shutdownAction = shutdownAction;

            InitializeToolTree();

            CloseCommand = new RelayCommand(CloseApplication);
            OpenImageFileCommand = new RelayCommand(OpenImageFile);
            RunAllCommand = new RelayCommand(async () => await RunAllTools(), () => !IsRunning && CurrentImage != null);
            RunSelectedCommand = new RelayCommand(RunSelectedTool, () => !IsRunning && SelectedTool != null && CurrentImage != null);
            ClearToolsCommand = new RelayCommand(ClearAllTools);
            RemoveToolCommand = new RelayCommand<ToolItem>(RemoveTool);
            MoveToolUpCommand = new RelayCommand<ToolItem>(MoveToolUp);
            MoveToolDownCommand = new RelayCommand<ToolItem>(MoveToolDown);
            TrainPatternCommand = new RelayCommand(TrainPattern, () => SelectedVisionTool is FeatureMatchTool);
            AutoTuneCommand = new RelayCommand(AutoTuneParameters, () => SelectedVisionTool is FeatureMatchTool);
            ClearResultImageCommand = new RelayCommand(ClearResultImage);
            AddStepCommand = new RelayCommand(AddStep, () => _recipeService.CurrentRecipe != null && SelectedCamera != null);
            DeleteStepCommand = new RelayCommand(DeleteStep, () => SelectedStep != null);
            MoveStepUpCommand = new RelayCommand(MoveStepUp, () => SelectedStep != null);
            MoveStepDownCommand = new RelayCommand(MoveStepDown, () => SelectedStep != null);
            LoadSamplePointCloudCommand = new RelayCommand(LoadSamplePointCloud);
            ShowCameraInfoCommand = new RelayCommand(ShowCameraInfo);
            AcquireImageCommand = new RelayCommand(async () => await AcquireImage(), () => SelectedCamera != null && !IsAcquiring);
            ConnectCameraCommand = new RelayCommand(async () => await ConnectCamera(), () => SelectedCamera != null && !IsCameraConnected);
            DisconnectCameraCommand = new RelayCommand(async () => await DisconnectCamera(), () => IsCameraConnected);
            GenerateHeightMapCommand = new RelayCommand(GenerateHeightMap, CanGenerateHeightMap);

            // 레시피 변경 이벤트 구독
            _recipeService.CurrentRecipeChanged += OnCurrentRecipeChanged;

            // 카메라 목록 로드
            LoadCameras();

            // 현재 레시피가 이미 로드되어 있으면 반영
            var currentRecipe = _recipeService.CurrentRecipe;
            if (currentRecipe != null)
            {
                CurrentRecipeName = currentRecipe.Name;
                RefreshSteps();
            }

            // Register for tool settings messages
            WeakReferenceMessenger.Default.Register<RequestTrainPatternMessage>(this, (r, m) =>
            {
                if (TrainPatternCommand.CanExecute(null))
                    TrainPatternCommand.Execute(null);
            });
            WeakReferenceMessenger.Default.Register<RequestAutoTuneMessage>(this, (r, m) =>
            {
                if (AutoTuneCommand.CanExecute(null))
                    AutoTuneCommand.Execute(null);
            });
        }
        #endregion

        #region Methods
        private void InitializeToolTree()
        {
            // Preprocessing 카테고리 (출력 채널 유지: 컬러→컬러, 그레이→그레이)
            var preprocessing = new ToolCategory { CategoryName = "Preprocessing (Color)" };
            preprocessing.Tools.Add(new ToolItem { Name = "Blur", ToolType = "BlurTool" });
            preprocessing.Tools.Add(new ToolItem { Name = "Morphology", ToolType = "MorphologyTool" });
            ToolTree.Add(preprocessing);

            // Conversion 카테고리 (출력: 항상 그레이스케일 1채널)
            var conversion = new ToolCategory { CategoryName = "Conversion (Gray)" };
            conversion.Tools.Add(new ToolItem { Name = "Grayscale", ToolType = "GrayscaleTool" });
            conversion.Tools.Add(new ToolItem { Name = "Threshold", ToolType = "ThresholdTool" });
            conversion.Tools.Add(new ToolItem { Name = "Edge Detection", ToolType = "EdgeDetectionTool" });
            conversion.Tools.Add(new ToolItem { Name = "Histogram", ToolType = "HistogramTool" });
            ToolTree.Add(conversion);

            // Pattern Matching 카테고리
            var patternMatching = new ToolCategory { CategoryName = "Pattern Matching" };
            patternMatching.Tools.Add(new ToolItem { Name = "Feature Match", ToolType = "FeatureMatchTool" });
            ToolTree.Add(patternMatching);

            // Blob Analysis 카테고리
            var blobAnalysis = new ToolCategory { CategoryName = "Blob Analysis" };
            blobAnalysis.Tools.Add(new ToolItem { Name = "Blob", ToolType = "BlobTool" });
            ToolTree.Add(blobAnalysis);

            // Measurement 카테고리
            var measurement = new ToolCategory { CategoryName = "Measurement" };
            measurement.Tools.Add(new ToolItem { Name = "Caliper", ToolType = "CaliperTool" });
            measurement.Tools.Add(new ToolItem { Name = "Line Fit", ToolType = "LineFitTool" });
            measurement.Tools.Add(new ToolItem { Name = "Circle Fit", ToolType = "CircleFitTool" });
            ToolTree.Add(measurement);

            // 3D Analysis 카테고리
            var threeD = new ToolCategory { CategoryName = "3D Analysis" };
            threeD.Tools.Add(new ToolItem { Name = "Height Slicer", ToolType = "HeightSlicerTool" });
            ToolTree.Add(threeD);
        }

        private void CloseApplication()
        {
            _shutdownAction();
        }

        private void OpenImageFile()
        {
            var filePath = _dialogService.ShowOpenFileDialog(
                "이미지 파일 열기",
                "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|All Files|*.*");

            if (filePath != null)
            {
                try
                {
                    var mat = Cv2.ImRead(filePath);
                    if (!mat.Empty())
                    {
                        CurrentImage = mat;
                        StatusMessage = $"이미지 로드 완료: {System.IO.Path.GetFileName(filePath)}";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"이미지 로드 실패: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"이미지 로드 실패: {ex.Message}");
                }
            }
        }

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
                OnPropertyChanged(nameof(SourceMat));  // ImageCanvas에 이미지 변경 알림
                OnPropertyChanged(nameof(DisplayMat));
            }
            catch
            {
                DisplayImage = null;
            }
        }

        /// <summary>
        /// 도구 드롭 처리 - 새 비전 도구 인스턴스 생성
        /// </summary>
        public ToolItem? CreateDroppedTool(ToolItem sourceTool, double x, double y)
        {
            var visionTool = VisionService.CreateTool(sourceTool.ToolType);
            if (visionTool == null)
                return null;

            var newTool = new ToolItem
            {
                Name = $"{sourceTool.Name} #{DroppedTools.Count(t => t.ToolType == sourceTool.ToolType) + 1}",
                ToolType = sourceTool.ToolType,
                X = x,
                Y = y,
                VisionTool = visionTool
            };

            visionTool.Name = newTool.Name;
            DroppedTools.Add(newTool);
            _visionService.AddTool(visionTool);

            return newTool;
        }

        /// <summary>
        /// 모든 도구 실행
        /// </summary>
        private async System.Threading.Tasks.Task RunAllTools()
        {
            if (CurrentImage == null || CurrentImage.Empty())
            {
                StatusMessage = "이미지가 로드되지 않았습니다.";
                return;
            }

            IsRunning = true;
            StatusMessage = "실행 중...";

            try
            {
                var results = await _visionService.ExecuteAllAsync();

                ExecutionTimeText = $"실행 시간: {_visionService.TotalExecutionTime:F2}ms";

                // VisionService에서 합성된 오버레이 사용
                var compositeOverlay = _visionService.LastCompositeOverlay;
                if (compositeOverlay != null && !compositeOverlay.Empty())
                {
                    ResultImage = compositeOverlay.ToWriteableBitmap();
                    ResultMat = compositeOverlay.Clone();
                    OverlayImage = ResultImage;
                    SelectedDisplayMode = ImageDisplayMode.ResultImage;
                }
                else if (results.LastOrDefault()?.OutputImage != null)
                {
                    ResultImage = results.Last().OutputImage!.ToWriteableBitmap();
                    ResultMat = results.Last().OutputImage!.Clone();
                    SelectedDisplayMode = ImageDisplayMode.ResultImage;
                }

                int successCount = results.Count(r => r.Success);
                StatusMessage = $"실행 완료: {successCount}/{results.Count} 성공";

                UpdateToolRunResults();
            }
            catch (Exception ex)
            {
                StatusMessage = $"실행 오류: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// 선택된 도구만 실행
        /// </summary>
        private void RunSelectedTool()
        {
            if (SelectedTool?.VisionTool == null || CurrentImage == null)
                return;

            IsRunning = true;
            StatusMessage = "실행 중...";

            try
            {
                var result = _visionService.ExecuteTool(SelectedTool.VisionTool, CurrentImage);

                ExecutionTimeText = $"실행 시간: {SelectedTool.VisionTool.ExecutionTime:F2}ms";

                if (result.OverlayImage != null)
                {
                    ResultImage = result.OverlayImage.ToWriteableBitmap();
                    ResultMat = result.OverlayImage.Clone();
                    OverlayImage = ResultImage;
                    SelectedDisplayMode = ImageDisplayMode.ResultImage;
                }
                else if (result.OutputImage != null)
                {
                    ResultImage = result.OutputImage.ToWriteableBitmap();
                    ResultMat = result.OutputImage.Clone();
                    SelectedDisplayMode = ImageDisplayMode.ResultImage;
                }

                StatusMessage = result.Success
                    ? $"실행 완료: {result.Message}"
                    : $"실행 실패: {result.Message}";

                UpdateToolRunResults();
            }
            catch (Exception ex)
            {
                StatusMessage = $"실행 오류: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// 도구 실행 결과를 ToolRunResults 컬렉션에 반영
        /// </summary>
        private void UpdateToolRunResults()
        {
            ToolRunResults.Clear();

            foreach (var toolItem in DroppedTools)
            {
                var visionTool = toolItem.VisionTool;
                if (visionTool == null) continue;

                var lastResult = visionTool.LastResult;
                var resultValue = string.Empty;

                if (lastResult != null)
                {
                    // Data 딕셔너리에서 주요 결과값을 문자열로 변환 (숫자는 소수점 3자리)
                    if (lastResult.Data != null && lastResult.Data.Count > 0)
                    {
                        var entries = lastResult.Data.Select(kv =>
                        {
                            var formatted = kv.Value switch
                            {
                                double d => d.ToString("F3"),
                                float f => f.ToString("F3"),
                                decimal m => m.ToString("F3"),
                                _ => kv.Value?.ToString() ?? ""
                            };
                            return $"{kv.Key}={formatted}";
                        });
                        resultValue = string.Join(", ", entries);
                    }
                    else
                    {
                        resultValue = lastResult.Message;
                    }
                }

                ToolRunResults.Add(new ToolResultItem
                {
                    ToolName = toolItem.Name,
                    Result = lastResult?.Success ?? false,
                    ResultValue = resultValue
                });
            }
        }

        /// <summary>
        /// 모든 도구 제거
        /// </summary>
        private void ClearAllTools()
        {
            ClearAllConnections();
            DroppedTools.Clear();
            _visionService.ClearTools();
            SelectedTool = null;
            StatusMessage = "모든 도구가 제거되었습니다.";
        }

        /// <summary>
        /// 특정 도구 제거
        /// </summary>
        private void RemoveTool(ToolItem? tool)
        {
            if (tool == null) return;

            // 해당 도구와 관련된 연결선 모두 제거
            RemoveConnectionsForTool(tool);

            if (tool.VisionTool != null)
                _visionService.RemoveTool(tool.VisionTool);

            DroppedTools.Remove(tool);

            if (SelectedTool == tool)
                SelectedTool = null;
        }

        #region Connection Management

        /// <summary>
        /// 도구 간 연결 추가
        /// </summary>
        public void AddConnection(ToolItem source, ToolItem target, ConnectionType type)
        {
            // 중복 연결 방지 (같은 소스, 타겟, 타입)
            var existing = Connections.FirstOrDefault(c =>
                c.SourceToolItem?.Id == source.Id &&
                c.TargetToolItem?.Id == target.Id &&
                c.Type == type);

            if (existing != null)
            {
                StatusMessage = $"이미 동일한 연결이 존재합니다: {source.Name} → {target.Name} ({type})";
                return;
            }

            var connection = new ToolConnection
            {
                SourceToolItem = source,
                TargetToolItem = target,
                Type = type
            };

            Connections.Add(connection);

            // VisionService에 연결 정보 등록
            if (source.VisionTool != null && target.VisionTool != null)
            {
                _visionService.AddConnection(source.VisionTool, target.VisionTool, type);
            }

            StatusMessage = $"연결 생성됨: {source.Name} → {target.Name} ({type})";
        }

        /// <summary>
        /// 특정 도구의 모든 연결 제거
        /// </summary>
        public void RemoveConnectionsForTool(ToolItem tool)
        {
            var toRemove = Connections
                .Where(c => c.SourceToolItem?.Id == tool.Id || c.TargetToolItem?.Id == tool.Id)
                .ToList();

            foreach (var conn in toRemove)
            {
                // VisionService에서도 제거
                if (conn.SourceToolItem?.VisionTool != null && conn.TargetToolItem?.VisionTool != null)
                {
                    _visionService.RemoveConnection(
                        conn.SourceToolItem.VisionTool,
                        conn.TargetToolItem.VisionTool,
                        conn.Type);
                }
                Connections.Remove(conn);
            }

            if (toRemove.Count > 0)
                StatusMessage = $"{tool.Name}의 연결 {toRemove.Count}개가 제거되었습니다.";
        }

        /// <summary>
        /// 모든 연결 제거
        /// </summary>
        private void ClearAllConnections()
        {
            Connections.Clear();
            _visionService.ClearConnections();
        }

        #endregion

        /// <summary>
        /// Result Image 초기화
        /// </summary>
        public void ClearResultImage()
        {
            ResultImage = null;
            ResultMat = null;
            OverlayImage = null;
            SelectedDisplayMode = ImageDisplayMode.OriginalImage;
            StatusMessage = "Result Image가 초기화되었습니다.";
        }

        /// <summary>
        /// 도구 순서 위로 이동
        /// </summary>
        private void MoveToolUp(ToolItem? tool)
        {
            if (tool == null) return;

            int index = DroppedTools.IndexOf(tool);
            if (index > 0)
            {
                DroppedTools.Move(index, index - 1);

                // 실행 큐도 동기화
                if (tool.VisionTool != null)
                {
                    int toolIndex = ExecutionQueue.IndexOf(tool.VisionTool);
                    if (toolIndex > 0)
                        _visionService.MoveTool(toolIndex, toolIndex - 1);
                }
            }
        }

        /// <summary>
        /// 도구 순서 아래로 이동
        /// </summary>
        private void MoveToolDown(ToolItem? tool)
        {
            if (tool == null) return;

            int index = DroppedTools.IndexOf(tool);
            if (index < DroppedTools.Count - 1)
            {
                DroppedTools.Move(index, index + 1);

                // 실행 큐도 동기화
                if (tool.VisionTool != null)
                {
                    int toolIndex = ExecutionQueue.IndexOf(tool.VisionTool);
                    if (toolIndex < ExecutionQueue.Count - 1)
                        _visionService.MoveTool(toolIndex, toolIndex + 1);
                }
            }
        }

        /// <summary>
        /// 패턴 학습 (FeatureMatchTool용)
        /// </summary>
        private void TrainPattern()
        {
            if (CurrentImage == null || CurrentImage.Empty())
            {
                StatusMessage = "이미지가 로드되지 않았습니다.";
                return;
            }

            // 도구에 UseROI가 설정되어 있으면 ROI 영역만 학습, 아니면 전체 이미지
            Mat trainingImage = CurrentImage;

            if (SelectedVisionTool != null && SelectedVisionTool.UseROI
                && SelectedVisionTool.ROI.Width > 0 && SelectedVisionTool.ROI.Height > 0)
            {
                var roi = SelectedVisionTool.ROI;
                // 이미지 범위 내로 클리핑
                int x = Math.Max(0, roi.X);
                int y = Math.Max(0, roi.Y);
                int w = Math.Min(roi.Width, CurrentImage.Width - x);
                int h = Math.Min(roi.Height, CurrentImage.Height - y);
                var rect = new CvRect(x, y, w, h);

                if (rect.Width > 10 && rect.Height > 10)
                {
                    trainingImage = new Mat(CurrentImage, rect);
                    StatusMessage = $"ROI 영역으로 학습 중... ({rect.Width}x{rect.Height})";
                }
            }

            if (SelectedVisionTool is FeatureMatchTool featureTool)
            {
                var targetModel = featureTool.SelectedModel;
                if (featureTool.TrainPattern(trainingImage, targetModel))
                {
                    if (targetModel != null)
                        StatusMessage = $"Model '{targetModel.Name}' 학습 완료";
                    else
                        StatusMessage = $"새 Model 학습 완료 (총 {featureTool.Models.Count}개)";
                }
                else
                {
                    StatusMessage = "Feature 학습 실패";
                }
            }
        }

        /// <summary>
        /// 파라미터 자동 튜닝 (FeatureMatchTool용)
        /// </summary>
        private void AutoTuneParameters()
        {
            if (CurrentImage == null || CurrentImage.Empty())
            {
                StatusMessage = "이미지가 로드되지 않았습니다.";
                return;
            }

            Mat tuningImage = CurrentImage;

            if (SelectedVisionTool != null && SelectedVisionTool.UseROI
                && SelectedVisionTool.ROI.Width > 0 && SelectedVisionTool.ROI.Height > 0)
            {
                var roi = SelectedVisionTool.ROI;
                int x = Math.Max(0, roi.X);
                int y = Math.Max(0, roi.Y);
                int w = Math.Min(roi.Width, CurrentImage.Width - x);
                int h = Math.Min(roi.Height, CurrentImage.Height - y);
                var rect = new CvRect(x, y, w, h);

                if (rect.Width > 10 && rect.Height > 10)
                    tuningImage = new Mat(CurrentImage, rect);
            }

            if (SelectedVisionTool is FeatureMatchTool featureTool)
            {
                featureTool.AutoTuneParameters(tuningImage);
                StatusMessage = "파라미터 자동 튜닝 완료";
            }
        }

        #region ROI Methods

        /// <summary>
        /// ROI 생성 이벤트 처리
        /// </summary>
        public void OnROICreated(ROIShape roi)
        {
            StatusMessage = $"ROI 생성됨: {roi.Name} ({roi.ShapeType})";

            // ROI가 생성되면 현재 선택된 도구에 자동 적용
            if (SelectedVisionTool != null)
            {
                ApplyROIToTool(SelectedVisionTool, roi);
            }
        }

        /// <summary>
        /// ROI 수정 이벤트 처리
        /// </summary>
        public void OnROIModified(ROIShape roi)
        {
            if (SelectedVisionTool == null) return;

            // Check if the modified shape is the SearchRegion
            if (SelectedVisionTool is FeatureMatchTool ft && ft.AssociatedSearchRegionShape == roi)
            {
                _isSyncingROI = true;
                try
                {
                    ft.SearchRegion = roi.GetBoundingRect();
                    ft.UseSearchRegion = true;
                }
                finally
                {
                    _isSyncingROI = false;
                }
                return;
            }

            // Otherwise, apply as regular ROI
            if (SelectedROI == roi)
            {
                ApplyROIToTool(SelectedVisionTool, roi);
            }
        }

        /// <summary>
        /// ROI 선택 변경 이벤트 처리
        /// </summary>
        public void OnROISelectionChanged(ROIShape? roi)
        {
            SelectedROI = roi;

            if (roi != null)
            {
                StatusMessage = $"ROI 선택됨: {roi.Name}";
            }
        }

        /// <summary>
        /// 선택된 ROI를 현재 도구에 적용
        /// </summary>
        private void ApplyROIToSelectedTool()
        {
            if (SelectedVisionTool != null && SelectedROI != null)
            {
                ApplyROIToTool(SelectedVisionTool, SelectedROI);
            }
        }

        /// <summary>
        /// ROI를 특정 도구에 적용
        /// </summary>
        private void ApplyROIToTool(VisionToolBase tool, ROIShape roi)
        {
            _isSyncingROI = true;
            try
            {
                var rect = roi.GetBoundingRect();
                tool.ROI = rect;
                tool.UseROI = true;
                tool.AssociatedROIShape = roi;

                StatusMessage = $"ROI 적용됨: {tool.Name} - ({rect.X}, {rect.Y}, {rect.Width}, {rect.Height})";
            }
            finally
            {
                _isSyncingROI = false;
            }
        }

        /// <summary>
        /// Search Region 생성 이벤트 처리 (FeatureMatchTool용)
        /// </summary>
        public void OnSearchRegionCreated(ROIShape roi)
        {
            roi.Color = System.Windows.Media.Colors.Cyan;
            roi.Name = "SearchRegion";

            if (SelectedVisionTool is FeatureMatchTool ft)
            {
                _isSyncingROI = true;
                try
                {
                    ft.SearchRegion = roi.GetBoundingRect();
                    ft.UseSearchRegion = true;
                    ft.AssociatedSearchRegionShape = roi;
                }
                finally
                {
                    _isSyncingROI = false;
                }

                // Show both ROI and SearchRegion on canvas
                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(ft.AssociatedROIShape));

                StatusMessage = $"Search Region 설정됨: ({ft.SearchRegion.X}, {ft.SearchRegion.Y}, {ft.SearchRegion.Width}, {ft.SearchRegion.Height})";
            }
        }

        /// <summary>
        /// Search Region 해제 (FeatureMatchTool용)
        /// </summary>
        public void ClearSearchRegion()
        {
            if (SelectedVisionTool is FeatureMatchTool ft)
            {
                ft.UseSearchRegion = false;
                ft.SearchRegion = new CvRect();
                ft.AssociatedSearchRegionShape = null;

                // Refresh canvas to show only the ROI
                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(ft.AssociatedROIShape));

                StatusMessage = "Search Region이 해제되었습니다.";
            }
        }

        /// <summary>
        /// 선택된 도구의 ROI 해제
        /// </summary>
        public void ClearToolROI()
        {
            if (SelectedVisionTool != null)
            {
                SelectedVisionTool.UseROI = false;
                StatusMessage = $"ROI 해제됨: {SelectedVisionTool.Name}";
            }
        }

        /// <summary>
        /// 선택된 도구의 ROI 프록시 속성 변경 시 캔버스 ROI 동기화
        /// (텍스트 필드 편집 → 캔버스 업데이트)
        /// </summary>
        private void OnSelectedToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isSyncingROI) return;
            if (sender is not VisionToolBase tool) return;

            // ROI 프록시 속성 변경 시 AssociatedROIShape 좌표 동기화
            if (e.PropertyName is nameof(VisionToolBase.ROIX) or nameof(VisionToolBase.ROIY)
                or nameof(VisionToolBase.ROIWidth) or nameof(VisionToolBase.ROIHeight))
            {
                if (tool.AssociatedROIShape is RectangleROI rectROI)
                {
                    rectROI.X = tool.ROIX;
                    rectROI.Y = tool.ROIY;
                    rectROI.Width = Math.Max(1, tool.ROIWidth);
                    rectROI.Height = Math.Max(1, tool.ROIHeight);
                    WeakReferenceMessenger.Default.Send(new RequestRefreshROIMessage(rectROI));
                }
            }

            // UseSearchRegion 토글 시 검색 영역 그래픽 표시/숨김
            if (tool is FeatureMatchTool ft2 &&
                e.PropertyName is nameof(FeatureMatchTool.UseSearchRegion))
            {
                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(ft2.AssociatedROIShape));
            }

            // SearchRegion 프록시 속성 변경 시 AssociatedSearchRegionShape 좌표 동기화
            if (tool is FeatureMatchTool ft &&
                e.PropertyName is nameof(FeatureMatchTool.SearchRegionX) or nameof(FeatureMatchTool.SearchRegionY)
                    or nameof(FeatureMatchTool.SearchRegionWidth) or nameof(FeatureMatchTool.SearchRegionHeight))
            {
                if (ft.AssociatedSearchRegionShape is RectangleROI searchRectROI)
                {
                    searchRectROI.X = ft.SearchRegionX;
                    searchRectROI.Y = ft.SearchRegionY;
                    searchRectROI.Width = Math.Max(1, ft.SearchRegionWidth);
                    searchRectROI.Height = Math.Max(1, ft.SearchRegionHeight);
                    WeakReferenceMessenger.Default.Send(new RequestRefreshROIMessage(searchRectROI));
                }
            }
        }

        #endregion

        #region Recipe / Camera / Step Methods

        private void OnCurrentRecipeChanged(object? sender, Recipe? recipe)
        {
            if (recipe != null)
            {
                CurrentRecipeName = recipe.Name;
                MigrateStepSequencing(recipe);
            }
            else
            {
                CurrentRecipeName = "No Recipe";
            }

            RefreshSteps();
            AddStepCommand.NotifyCanExecuteChanged();
        }

        private void LoadCameras()
        {
            Cameras.Clear();
            _cameraService.LoadCameraRegistry();
            foreach (var cam in _cameraService.GetAllCameras())
            {
                Cameras.Add(cam);
            }
        }

        private void RefreshSteps()
        {
            Steps.Clear();
            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;

            foreach (var step in recipe.Steps.OrderBy(s => s.Sequence))
            {
                // 카메라 필터가 없거나 선택된 카메라와 일치하는 스텝만 표시
                if (SelectedCamera == null || string.IsNullOrEmpty(step.CameraId) || step.CameraId == SelectedCamera.Id)
                {
                    Steps.Add(step);
                }
            }
        }

        private void AddStep()
        {
            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null || SelectedCamera == null) return;

            var step = _recipeService.AddStep(recipe, SelectedCamera.Id);
            if (step != null)
            {
                int camIdx = GetCameraDisplayIndex(SelectedCamera.Id);
                step.Name = $"{camIdx}-{step.Sequence}";
                RefreshSteps();
                SelectedStep = step;
                StatusMessage = $"Step added: {step.Name}";
            }
        }

        private void DeleteStep()
        {
            if (SelectedStep == null) return;

            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;

            if (_dialogService.ShowConfirmation(
                $"'{SelectedStep.Name}'을(를) 삭제하시겠습니까?",
                "Delete Step"))
            {
                var cameraId = SelectedStep.CameraId;
                _recipeService.RemoveStep(recipe, SelectedStep.Id);
                RenameStepsForCamera(recipe, cameraId);
                SelectedStep = null;
                ClearAllTools();
                RefreshSteps();
                StatusMessage = "Step deleted";
            }
        }

        private void MoveStepUp()
        {
            if (SelectedStep == null) return;

            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;

            if (SelectedStep.Sequence <= 1) return;

            var stepToMove = SelectedStep;
            _recipeService.MoveStep(recipe, stepToMove.Id, stepToMove.Sequence - 1);
            RenameStepsForCamera(recipe, stepToMove.CameraId);
            RefreshSteps();
            SelectedStep = stepToMove;
        }

        private void MoveStepDown()
        {
            if (SelectedStep == null) return;

            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;

            int cameraStepCount = recipe.Steps.Count(s => s.CameraId == SelectedStep.CameraId);
            if (SelectedStep.Sequence >= cameraStepCount) return;

            var stepToMove = SelectedStep;
            _recipeService.MoveStep(recipe, stepToMove.Id, stepToMove.Sequence + 1);
            RenameStepsForCamera(recipe, stepToMove.CameraId);
            RefreshSteps();
            SelectedStep = stepToMove;
        }

        /// <summary>
        /// 스텝의 도구와 연결을 워크스페이스에 로드
        /// </summary>
        public void LoadStepToWorkspace(InspectionStep step)
        {
            // 기존 워크스페이스 정리
            ClearAllConnections();
            DroppedTools.Clear();
            _visionService.ClearTools();
            SelectedTool = null;

            // ToolConfig → VisionToolBase 역직렬화
            var toolConfigs = step.Tools.OrderBy(t => t.Sequence).ToList();
            var configToToolItem = new Dictionary<string, ToolItem>();

            foreach (var config in toolConfigs)
            {
                if (!config.IsEnabled) continue;

                var visionTool = ToolSerializer.DeserializeTool(config);
                if (visionTool == null) continue;

                var toolItem = new ToolItem
                {
                    Name = config.Name,
                    ToolType = config.ToolType,
                    X = config.X,
                    Y = config.Y,
                    VisionTool = visionTool
                };

                DroppedTools.Add(toolItem);
                _visionService.AddTool(visionTool);
                configToToolItem[config.Id] = toolItem;
            }

            // 연결 복원
            foreach (var config in toolConfigs)
            {
                if (!configToToolItem.ContainsKey(config.Id)) continue;
                var targetToolItem = configToToolItem[config.Id];

                foreach (var connConfig in config.Connections)
                {
                    if (configToToolItem.TryGetValue(connConfig.SourceToolId, out var sourceToolItem))
                    {
                        var connType = connConfig.ConnectionType switch
                        {
                            "Image" => ConnectionType.Image,
                            "Coordinates" => ConnectionType.Coordinates,
                            "Result" => ConnectionType.Result,
                            _ => ConnectionType.Image
                        };

                        AddConnection(sourceToolItem, targetToolItem, connType);
                    }
                }
            }

            StatusMessage = $"Step loaded: {step.Name} ({DroppedTools.Count} tools)";
        }

        /// <summary>
        /// 현재 워크스페이스의 도구와 연결을 선택된 스텝에 저장
        /// </summary>
        public void SaveWorkspaceToStep()
        {
            if (SelectedStep == null) return;

            SelectedStep.Tools.Clear();

            // 도구 ID 매핑 (ToolItem.Id → ToolConfig.Id)
            var toolItemToConfigId = new Dictionary<string, string>();

            foreach (var toolItem in DroppedTools)
            {
                if (toolItem.VisionTool == null) continue;

                var config = ToolSerializer.SerializeTool(toolItem.VisionTool);
                config.X = toolItem.X;
                config.Y = toolItem.Y;
                config.Connections = new List<ToolConnectionConfig>();

                toolItemToConfigId[toolItem.Id] = config.Id;
                SelectedStep.Tools.Add(config);
            }

            // 연결 정보 저장
            foreach (var conn in Connections)
            {
                if (conn.SourceToolItem == null || conn.TargetToolItem == null) continue;

                if (!toolItemToConfigId.TryGetValue(conn.SourceToolItem.Id, out var sourceConfigId)) continue;
                if (!toolItemToConfigId.TryGetValue(conn.TargetToolItem.Id, out var targetConfigId)) continue;

                var targetConfig = SelectedStep.Tools.FirstOrDefault(t => t.Id == targetConfigId);
                if (targetConfig != null)
                {
                    targetConfig.Connections.Add(new ToolConnectionConfig
                    {
                        SourceToolId = sourceConfigId,
                        ConnectionType = conn.Type.ToString()
                    });
                }
            }

            StatusMessage = $"Step saved: {SelectedStep.Name} ({SelectedStep.Tools.Count} tools)";
        }

        private int GetCameraDisplayIndex(string? cameraId)
        {
            if (string.IsNullOrEmpty(cameraId)) return 0;
            for (int i = 0; i < Cameras.Count; i++)
            {
                if (Cameras[i].Id == cameraId) return i + 1;
            }
            return 0;
        }

        private void RenameStepsForCamera(Recipe recipe, string cameraId)
        {
            int camIdx = GetCameraDisplayIndex(cameraId);
            var cameraSteps = recipe.Steps
                .Where(s => s.CameraId == cameraId)
                .OrderBy(s => s.Sequence)
                .ToList();
            for (int i = 0; i < cameraSteps.Count; i++)
            {
                cameraSteps[i].Name = camIdx > 0 ? $"{camIdx}-{i + 1}" : $"Step {i + 1}";
            }
        }

        private void MigrateStepSequencing(Recipe recipe)
        {
            var groups = recipe.Steps.GroupBy(s => s.CameraId);
            foreach (var group in groups)
            {
                var steps = group.OrderBy(s => s.Sequence).ToList();
                int camIdx = GetCameraDisplayIndex(group.Key);
                for (int i = 0; i < steps.Count; i++)
                {
                    steps[i].Sequence = i + 1;
                    if (camIdx > 0)
                        steps[i].Name = $"{camIdx}-{i + 1}";
                }
            }
        }

        #endregion

        #region Camera Connection & Acquisition

        private void ShowCameraInfo()
        {
            IsCameraInfoPopupOpen = !IsCameraInfoPopupOpen;
        }

        private async System.Threading.Tasks.Task ConnectCamera()
        {
            if (SelectedCamera == null) return;

            try
            {
                _cameraAcquisition?.Dispose();
                _cameraAcquisition = CameraAcquisitionFactory.Create(SelectedCamera);

                var success = await _cameraAcquisition.ConnectAsync(SelectedCamera);
                IsCameraConnected = success;

                StatusMessage = success
                    ? $"카메라 연결됨: {SelectedCamera.Name}"
                    : $"카메라 연결 실패: {SelectedCamera.Name}";

                ConnectCameraCommand.NotifyCanExecuteChanged();
                DisconnectCameraCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"카메라 연결 오류: {ex.Message}";
                IsCameraConnected = false;
            }
        }

        private async System.Threading.Tasks.Task DisconnectCamera()
        {
            try
            {
                if (_cameraAcquisition != null)
                {
                    await _cameraAcquisition.DisconnectAsync();
                    _cameraAcquisition.Dispose();
                    _cameraAcquisition = null;
                }

                IsCameraConnected = false;
                StatusMessage = "카메라 연결 해제됨";

                ConnectCameraCommand.NotifyCanExecuteChanged();
                DisconnectCameraCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"카메라 연결 해제 오류: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task AcquireImage()
        {
            if (SelectedCamera == null) return;

            IsAcquiring = true;
            AcquireImageCommand.NotifyCanExecuteChanged();

            try
            {
                // Auto-connect if not connected
                if (!IsCameraConnected)
                {
                    await ConnectCamera();
                    if (!IsCameraConnected)
                    {
                        StatusMessage = "카메라 연결 실패로 획득 중단";
                        return;
                    }
                }

                var result = await _cameraAcquisition!.AcquireAsync();

                if (result.Success && result.Image2D != null)
                {
                    CurrentImage = result.Image2D;

                    // 3D 포인트 클라우드가 있으면 적용
                    if (result.PointCloud != null)
                    {
                        CurrentPointCloud = result.PointCloud;
                    }

                    StatusMessage = result.Message;
                }
                else
                {
                    StatusMessage = $"이미지 획득 실패: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"이미지 획득 오류: {ex.Message}";
            }
            finally
            {
                IsAcquiring = false;
                AcquireImageCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// 카메라 목록을 CameraService에서 다시 로드
        /// </summary>
        public void RefreshCamerasFromService()
        {
            var selectedId = SelectedCamera?.Id;
            LoadCameras();

            // 이전에 선택된 카메라 복원
            if (selectedId != null)
            {
                SelectedCamera = Cameras.FirstOrDefault(c => c.Id == selectedId);
            }
        }

        #endregion

        #region 3D Point Cloud

        /// <summary>
        /// 샘플 포인트 클라우드 데이터 생성 (200x200 격자 지형)
        /// </summary>
        private void LoadSamplePointCloud()
        {
            const int gridSize = 200;
            int count = gridSize * gridSize;
            var positions = new Vector3[count];
            var colors = new System.Windows.Media.Color[count];
            float spacing = 1.0f;
            float offsetX = -gridSize * spacing * 0.5f;
            float offsetZ = -gridSize * spacing * 0.5f;

            int idx = 0;
            for (int iz = 0; iz < gridSize; iz++)
            {
                for (int ix = 0; ix < gridSize; ix++)
                {
                    float x = offsetX + ix * spacing;
                    float z = offsetZ + iz * spacing;

                    float y = 30f * MathF.Sin(x * 0.05f) * MathF.Cos(z * 0.05f)
                            + 15f * MathF.Sin(x * 0.1f + 1f)
                            + 10f * MathF.Cos(z * 0.08f + 2f)
                            + 5f * MathF.Sin((x + z) * 0.15f);

                    positions[idx] = new Vector3(x, y, z);
                    colors[idx] = System.Windows.Media.Color.FromRgb(200, 200, 200);
                    idx++;
                }
            }

            CurrentPointCloud = new PointCloudData
            {
                Name = "Sample Terrain",
                Positions = positions,
                Colors = colors,
                GridWidth = gridSize,
                GridHeight = gridSize
            };

            StatusMessage = $"3D 샘플 지형 데이터 로드 완료: {count:N0} points";
        }

        partial void OnCurrentPointCloudChanged(PointCloudData? value)
        {
            if (value != null && value.Positions.Length > 0)
            {
                float yMin = float.MaxValue;
                float yMax = float.MinValue;
                foreach (var pos in value.Positions)
                {
                    if (pos.Y < yMin) yMin = pos.Y;
                    if (pos.Y > yMax) yMax = pos.Y;
                }

                PointCloudYMin = yMin;
                PointCloudYMax = yMax;
                HeightBaseline = 0f;
                HeightLowerLimit = yMin;
                HeightUpperLimit = yMax;
            }

            GenerateHeightMapCommand.NotifyCanExecuteChanged();
        }

        private bool CanGenerateHeightMap() => CurrentPointCloud?.IsOrganized == true;

        private void GenerateHeightMap()
        {
            if (CurrentPointCloud == null || !CurrentPointCloud.IsOrganized)
                return;

            try
            {
                var (heightMap, metadata) = _visionService.GenerateHeightMap(
                    CurrentPointCloud, HeightBaseline, HeightLowerLimit, HeightUpperLimit);

                CurrentImage = heightMap;
                CurrentHeightMapMetadata = metadata;

                StatusMessage = $"Height Map 생성 완료: {metadata.Width}x{metadata.Height} " +
                    $"(Baseline={HeightBaseline:F1}, Range=[{HeightLowerLimit:F1}, {HeightUpperLimit:F1}])";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Height Map 생성 실패: {ex.Message}";
            }
        }

        #endregion

        /// <summary>
        /// 명령의 CanExecute 상태 갱신
        /// </summary>
        private void NotifyCommandsCanExecuteChanged()
        {
            RunAllCommand.NotifyCanExecuteChanged();
            RunSelectedCommand.NotifyCanExecuteChanged();
            TrainPatternCommand.NotifyCanExecuteChanged();
            AutoTuneCommand.NotifyCanExecuteChanged();
            AddStepCommand.NotifyCanExecuteChanged();
            DeleteStepCommand.NotifyCanExecuteChanged();
            MoveStepUpCommand.NotifyCanExecuteChanged();
            MoveStepDownCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// 도구에 맞는 ToolSettingsViewModel 생성
        /// </summary>
        private static ToolSettingsViewModelBase? CreateToolSettingsViewModel(VisionToolBase tool)
        {
            return tool switch
            {
                GrayscaleTool t => new GrayscaleToolSettingsViewModel(t),
                BlurTool t => new BlurToolSettingsViewModel(t),
                ThresholdTool t => new ThresholdToolSettingsViewModel(t),
                EdgeDetectionTool t => new EdgeDetectionToolSettingsViewModel(t),
                MorphologyTool t => new MorphologyToolSettingsViewModel(t),
                HistogramTool t => new HistogramToolSettingsViewModel(t),
                FeatureMatchTool t => new FeatureMatchToolSettingsViewModel(t),
                BlobTool t => new BlobToolSettingsViewModel(t),
                CaliperTool t => new CaliperToolSettingsViewModel(t),
                LineFitTool t => new LineFitToolSettingsViewModel(t),
                CircleFitTool t => new CircleFitToolSettingsViewModel(t),
                HeightSlicerTool t => new HeightSlicerToolSettingsViewModel(t),
                _ => null
            };
        }

        #endregion

        #region Recipe & Camera Manager Commands

        public Recipe? GetCurrentRecipe() => _recipeService.CurrentRecipe;

        public void SaveCurrentRecipe()
        {
            var currentRecipe = _recipeService.CurrentRecipe;
            if (currentRecipe != null)
            {
                SaveWorkspaceToStep();
                currentRecipe.ModifiedAt = DateTime.Now;
                _recipeService.SaveRecipe(currentRecipe);
                StatusMessage = $"Recipe saved: {currentRecipe.Name}";
            }
            else
            {
                _dialogService.ShowInformation(
                    "저장할 레시피가 없습니다. Recipe Manager에서 레시피를 로드하세요.",
                    "No Recipe");
            }
        }

        public void OpenRecipeManager()
        {
            var loadedRecipe = _dialogService.ShowRecipeManagerDialog();
            if (loadedRecipe != null)
            {
                CurrentRecipeName = loadedRecipe.Name;
                StatusMessage = $"Recipe loaded: {loadedRecipe.Name}";
            }
        }

        public void OpenCameraManager()
        {
            _dialogService.ShowCameraManagerDialog();
            RefreshCamerasFromService();
        }

        public void RenameTool(ToolItem tool)
        {
            if (tool == null) return;

            var newName = _dialogService.ShowRenameDialog(tool.Name);
            if (newName != null)
            {
                tool.Name = newName;
                if (tool.VisionTool != null)
                {
                    tool.VisionTool.Name = newName;
                }
            }
        }

        #endregion
    }
}
