using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.PLC.Services;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>
    /// 시퀀스 에디터 ViewModel — 통합 프로세스 시퀀스 편집 + PLC I/O 모니터링
    /// </summary>
    public partial class SequenceEditorViewModel : ObservableObject
    {
        private readonly IRecipeService _recipeService;
        private readonly ICameraService _cameraService;
        private readonly IDialogService _dialogService;

        public ObservableCollection<SequenceNodeItem> Nodes { get; } = new();
        public ObservableCollection<SequenceEdgeItem> Edges { get; } = new();

        [ObservableProperty]
        private SequenceNodeItem? _selectedNode;

        [ObservableProperty]
        private string _sequenceName = "Process Sequence";

        /// <summary>Reset 신호 PLC 주소. 조건 충족 시 시퀀스를 Start로 즉시 복귀</summary>
        [ObservableProperty]
        private string _resetSignalAddress = string.Empty;

        /// <summary>Reset 신호 체크 모드</summary>
        [ObservableProperty]
        private InputCheckMode _resetSignalCheckMode = InputCheckMode.BitOn;

        /// <summary>Reset 신호 Word 비교값</summary>
        [ObservableProperty]
        private int? _resetSignalCompareValue;

        /// <summary>InputCheckMode enum values for Reset ComboBox</summary>
        public Array ResetCheckModes => Enum.GetValues(typeof(InputCheckMode));

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        /// <summary>사용 가능한 카메라 ID 목록 (Inspection 노드의 CameraId 선택용)</summary>
        public ObservableCollection<string> AvailableCameraIds { get; } = new();

        /// <summary>노드 팔레트 아이템</summary>
        public ObservableCollection<NodePaletteItem> PaletteItems { get; } = new();

        /// <summary>연결 시작 노드 (연결 모드)</summary>
        private SequenceNodeItem? _connectionSource;
        public bool IsConnecting => _connectionSource != null;

        // --- File Save/Load ---

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        private const string SequenceFileFilter = "시퀀스 파일 (*.seq.json)|*.seq.json|JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*";

        /// <summary>시스템 레벨 시퀀스 파일 경로 (AppData)</summary>
        private static readonly string SystemSequencePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BODA VISION AI", "process_sequence.json");

        /// <summary>현재 열린 파일 경로 (null이면 시스템 시퀀스)</summary>
        [ObservableProperty]
        private string? _currentFilePath;

        // --- PLC Monitor ---

        private IPlcConnection? _plcConnection;
        private PlcConnectionConfig? _plcConfig;
        private CancellationTokenSource? _monitorCts;

        /// <summary>PLC I/O 모니터 항목 목록</summary>
        public ObservableCollection<PlcMonitorItem> MonitorItems { get; } = new();

        [ObservableProperty]
        private bool _isPlcConnected;

        [ObservableProperty]
        private bool _isPlcMonitoring;

        [ObservableProperty]
        private bool _isMonitorPanelVisible;

        [ObservableProperty]
        private string _plcConnectionStatus = "미연결";

        // --- Sequence Test Run ---

        private CancellationTokenSource? _testRunCts;

        [ObservableProperty]
        private bool _isTestRunning;

        [ObservableProperty]
        private string? _activeNodeName;

        [ObservableProperty]
        private int _testCycleCount;

        public SequenceEditorViewModel(IRecipeService recipeService, ICameraService cameraService, IDialogService dialogService)
        {
            _recipeService = recipeService;
            _cameraService = cameraService;
            _dialogService = dialogService;

            InitializePalette();
            LoadAvailableCameras();
            LoadSystemSequence();
        }

        private void InitializePalette()
        {
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.Start, "Start", "#4CAF50"));
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.End, "End", "#F44336"));
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.InputCheck, "Input Check", "#2196F3"));
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.OutputAction, "Output Action", "#FF9800"));
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.Inspection, "Inspection", "#9C27B0"));
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.Branch, "Branch", "#FFEB3B"));
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.Delay, "Delay", "#9E9E9E"));
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.Repeat, "Repeat", "#00BCD4"));
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.RecipeChange, "Recipe Change", "#E91E63"));
            PaletteItems.Add(new NodePaletteItem(SequenceNodeType.StepChange, "Step Change", "#00897B"));
        }

        private void LoadAvailableCameras()
        {
            AvailableCameraIds.Clear();
            var cameras = _cameraService.GetAllCameras();
            foreach (var cam in cameras)
                AvailableCameraIds.Add(cam.Id);
        }

        [RelayCommand]
        private void AddNode(NodePaletteItem? paletteItem)
        {
            if (paletteItem == null) return;

            var config = new SequenceNodeConfig
            {
                NodeType = paletteItem.NodeType,
                Name = paletteItem.DisplayName,
                X = 200,
                Y = 50 + (Nodes.Count * 110)
            };

            var nodeItem = new SequenceNodeItem(config);
            Nodes.Add(nodeItem);
            SelectedNode = nodeItem;
            StatusMessage = $"노드 추가: {config.Name}";
        }

        [RelayCommand]
        private void DeleteNode(SequenceNodeItem? node)
        {
            if (node == null) return;

            // 연결된 엣지 제거
            var connectedEdges = Edges.Where(e => e.SourceNode?.Id == node.Id || e.TargetNode?.Id == node.Id).ToList();
            foreach (var edge in connectedEdges)
                Edges.Remove(edge);

            // 다른 노드의 참조도 정리
            foreach (var other in Nodes)
            {
                if (other.Config.NextNodeId == node.Id)
                    other.Config.NextNodeId = null;
                if (other.Config.TrueBranchNodeId == node.Id)
                    other.Config.TrueBranchNodeId = null;
                if (other.Config.FalseBranchNodeId == node.Id)
                    other.Config.FalseBranchNodeId = null;
                if (other.Config.RepeatTargetNodeId == node.Id)
                    other.Config.RepeatTargetNodeId = null;
            }

            Nodes.Remove(node);
            if (SelectedNode == node)
                SelectedNode = null;

            StatusMessage = $"노드 삭제: {node.Name}";
        }

        [RelayCommand]
        private void StartConnection(SequenceNodeItem? source)
        {
            if (source == null) return;
            _connectionSource = source;
            OnPropertyChanged(nameof(IsConnecting));
            StatusMessage = $"연결 시작: {source.Name} → (대상 노드를 클릭하세요)";
        }

        [RelayCommand]
        private void CompleteConnection(SequenceNodeItem? target)
        {
            if (_connectionSource == null || target == null || _connectionSource == target) return;

            // 중복 연결 방지
            var exists = Edges.Any(e =>
                e.SourceNode?.Id == _connectionSource.Id && e.TargetNode?.Id == target.Id);
            if (exists)
            {
                StatusMessage = "이미 연결되어 있습니다.";
                CancelConnection();
                return;
            }

            // 라벨 결정
            string? label = "Next";
            if (_connectionSource.NodeType == SequenceNodeType.Branch)
            {
                if (string.IsNullOrEmpty(_connectionSource.Config.TrueBranchNodeId))
                {
                    label = "True";
                    _connectionSource.Config.TrueBranchNodeId = target.Id;
                }
                else if (string.IsNullOrEmpty(_connectionSource.Config.FalseBranchNodeId))
                {
                    label = "False";
                    _connectionSource.Config.FalseBranchNodeId = target.Id;
                }
                else
                {
                    StatusMessage = "Branch 노드는 True/False 두 개의 연결만 가능합니다.";
                    CancelConnection();
                    return;
                }
            }
            else if (_connectionSource.NodeType == SequenceNodeType.Repeat)
            {
                _connectionSource.Config.RepeatTargetNodeId = target.Id;
                label = "Repeat";
            }
            else
            {
                _connectionSource.Config.NextNodeId = target.Id;
            }

            var edgeConfig = new SequenceEdgeConfig
            {
                SourceNodeId = _connectionSource.Id,
                TargetNodeId = target.Id,
                Label = label
            };
            var edgeItem = new SequenceEdgeItem(edgeConfig, _connectionSource, target);
            Edges.Add(edgeItem);

            StatusMessage = $"연결 완료: {_connectionSource.Name} → {target.Name} ({label})";
            _connectionSource = null;
            OnPropertyChanged(nameof(IsConnecting));
        }

        [RelayCommand]
        private void CancelConnection()
        {
            _connectionSource = null;
            OnPropertyChanged(nameof(IsConnecting));
            StatusMessage = "연결 취소됨";
        }

        [RelayCommand]
        private void DeleteEdge(SequenceEdgeItem? edge)
        {
            if (edge == null) return;

            // 소스 노드의 참조 정리
            if (edge.SourceNode != null)
            {
                var sourceConfig = edge.SourceNode.Config;
                if (sourceConfig.NextNodeId == edge.TargetNode?.Id)
                    sourceConfig.NextNodeId = null;
                if (sourceConfig.TrueBranchNodeId == edge.TargetNode?.Id)
                    sourceConfig.TrueBranchNodeId = null;
                if (sourceConfig.FalseBranchNodeId == edge.TargetNode?.Id)
                    sourceConfig.FalseBranchNodeId = null;
                if (sourceConfig.RepeatTargetNodeId == edge.TargetNode?.Id)
                    sourceConfig.RepeatTargetNodeId = null;
            }

            Edges.Remove(edge);
            StatusMessage = "연결선 삭제됨";
        }

        [RelayCommand]
        private void GenerateDefaultSequence()
        {
            if (AvailableCameraIds.Count == 0)
            {
                _dialogService.ShowWarning("레시피에 등록된 카메라가 없습니다.", "시퀀스 생성");
                return;
            }

            if (Nodes.Count > 0)
            {
                if (!_dialogService.ShowConfirmation(
                    "기존 시퀀스를 삭제하고 디폴트 시퀀스를 생성하시겠습니까?",
                    "시퀀스 생성"))
                    return;
            }

            Nodes.Clear();
            Edges.Clear();
            SelectedNode = null;

            int idx = 0;
            double branchTrueX = 80;
            double branchFalseX = 320;
            double spacing = 110;

            SequenceNodeConfig CreateNodeCfg(SequenceNodeType type, string name, double nodeX = 200)
            {
                var cfg = new SequenceNodeConfig
                {
                    NodeType = type,
                    Name = name,
                    X = nodeX,
                    Y = 50 + (idx * spacing)
                };
                idx++;
                return cfg;
            }

            // Start
            var startCfg = CreateNodeCfg(SequenceNodeType.Start, "Start");

            // Recipe Change (신호 확인 → 인덱스 읽기 → 비교/변경)
            var recipeChangeCfg = CreateNodeCfg(SequenceNodeType.RecipeChange, "Recipe Change");

            // Wait Trigger
            var waitTriggerCfg = CreateNodeCfg(SequenceNodeType.InputCheck, "Wait Trigger");
            waitTriggerCfg.CheckMode = InputCheckMode.BitOn;

            // Step Change (신호 확인 → 스텝 인덱스 읽기 → 카메라 스텝 설정)
            var stepChangeCfg = CreateNodeCfg(SequenceNodeType.StepChange, "Step Change");

            // Busy ON
            var busyOnCfg = CreateNodeCfg(SequenceNodeType.OutputAction, "Busy ON");
            busyOnCfg.OutputDataType = PLC.Models.PlcDataType.Bit;
            busyOnCfg.BitValue = true;

            // Inspection nodes (one per camera)
            var inspectionCfgs = new System.Collections.Generic.List<SequenceNodeConfig>();
            foreach (var camId in AvailableCameraIds)
            {
                var inspCfg = CreateNodeCfg(SequenceNodeType.Inspection, $"Inspection ({camId})");
                inspCfg.CameraId = camId;
                inspectionCfgs.Add(inspCfg);
            }

            // Branch (AllCameras)
            var branchCfg = CreateNodeCfg(SequenceNodeType.Branch, "Result Branch");
            branchCfg.BranchOnAllCameras = true;

            // Result OK ON
            var resultOkCfg = CreateNodeCfg(SequenceNodeType.OutputAction, "Result OK ON", branchTrueX);
            resultOkCfg.OutputDataType = PLC.Models.PlcDataType.Bit;
            resultOkCfg.BitValue = true;

            // Result NG ON
            var resultNgCfg = CreateNodeCfg(SequenceNodeType.OutputAction, "Result NG ON", branchFalseX);
            resultNgCfg.OutputDataType = PLC.Models.PlcDataType.Bit;
            resultNgCfg.BitValue = true;

            // Complete ON
            var completeOnCfg = CreateNodeCfg(SequenceNodeType.OutputAction, "Complete ON");
            completeOnCfg.OutputDataType = PLC.Models.PlcDataType.Bit;
            completeOnCfg.BitValue = true;

            // Wait Ack
            var waitAckCfg = CreateNodeCfg(SequenceNodeType.InputCheck, "Wait Ack");
            waitAckCfg.CheckMode = InputCheckMode.BitOff;
            waitAckCfg.TimeoutMs = 30000;

            // Busy OFF
            var busyOffCfg = CreateNodeCfg(SequenceNodeType.OutputAction, "Busy OFF");
            busyOffCfg.OutputDataType = PLC.Models.PlcDataType.Bit;
            busyOffCfg.BitValue = false;

            // Complete OFF
            var completeOffCfg = CreateNodeCfg(SequenceNodeType.OutputAction, "Complete OFF");
            completeOffCfg.OutputDataType = PLC.Models.PlcDataType.Bit;
            completeOffCfg.BitValue = false;

            // Repeat
            var repeatCfg = CreateNodeCfg(SequenceNodeType.Repeat, "Repeat");
            repeatCfg.RepeatCount = -1;

            // End
            var endCfg = CreateNodeCfg(SequenceNodeType.End, "End");

            // 흐름 연결
            startCfg.NextNodeId = recipeChangeCfg.Id;
            recipeChangeCfg.NextNodeId = waitTriggerCfg.Id;
            waitTriggerCfg.NextNodeId = stepChangeCfg.Id;
            stepChangeCfg.NextNodeId = busyOnCfg.Id;

            // Busy ON → first Inspection
            busyOnCfg.NextNodeId = inspectionCfgs[0].Id;

            // Chain inspections
            for (int i = 0; i < inspectionCfgs.Count - 1; i++)
                inspectionCfgs[i].NextNodeId = inspectionCfgs[i + 1].Id;

            // Last inspection → Branch
            inspectionCfgs[^1].NextNodeId = branchCfg.Id;

            // Branch
            branchCfg.TrueBranchNodeId = resultOkCfg.Id;
            branchCfg.FalseBranchNodeId = resultNgCfg.Id;

            // OK/NG → Complete
            resultOkCfg.NextNodeId = completeOnCfg.Id;
            resultNgCfg.NextNodeId = completeOnCfg.Id;

            // Complete → Ack → Clear → Repeat
            completeOnCfg.NextNodeId = waitAckCfg.Id;
            waitAckCfg.NextNodeId = busyOffCfg.Id;
            busyOffCfg.NextNodeId = completeOffCfg.Id;
            completeOffCfg.NextNodeId = repeatCfg.Id;
            repeatCfg.RepeatTargetNodeId = recipeChangeCfg.Id;
            repeatCfg.NextNodeId = endCfg.Id;

            // Build all configs list
            var allConfigs = new System.Collections.Generic.List<SequenceNodeConfig>
            {
                startCfg, recipeChangeCfg, waitTriggerCfg, stepChangeCfg, busyOnCfg
            };
            allConfigs.AddRange(inspectionCfgs);
            allConfigs.AddRange(new[]
            {
                branchCfg, resultOkCfg, resultNgCfg, completeOnCfg,
                waitAckCfg, busyOffCfg, completeOffCfg, repeatCfg, endCfg
            });

            foreach (var cfg in allConfigs)
                Nodes.Add(new SequenceNodeItem(cfg));

            var nodeDict = Nodes.ToDictionary(n => n.Id);

            // 엣지 생성
            void AddEdge(string sourceId, string targetId, string label)
            {
                if (nodeDict.TryGetValue(sourceId, out var src) && nodeDict.TryGetValue(targetId, out var tgt))
                {
                    var ec = new SequenceEdgeConfig { SourceNodeId = sourceId, TargetNodeId = targetId, Label = label };
                    Edges.Add(new SequenceEdgeItem(ec, src, tgt));
                }
            }

            AddEdge(startCfg.Id, recipeChangeCfg.Id, "Next");
            AddEdge(recipeChangeCfg.Id, waitTriggerCfg.Id, "Next");
            AddEdge(waitTriggerCfg.Id, stepChangeCfg.Id, "Next");
            AddEdge(stepChangeCfg.Id, busyOnCfg.Id, "Next");
            AddEdge(busyOnCfg.Id, inspectionCfgs[0].Id, "Next");

            for (int i = 0; i < inspectionCfgs.Count - 1; i++)
                AddEdge(inspectionCfgs[i].Id, inspectionCfgs[i + 1].Id, "Next");

            AddEdge(inspectionCfgs[^1].Id, branchCfg.Id, "Next");
            AddEdge(branchCfg.Id, resultOkCfg.Id, "True");
            AddEdge(branchCfg.Id, resultNgCfg.Id, "False");
            AddEdge(resultOkCfg.Id, completeOnCfg.Id, "Next");
            AddEdge(resultNgCfg.Id, completeOnCfg.Id, "Next");
            AddEdge(completeOnCfg.Id, waitAckCfg.Id, "Next");
            AddEdge(waitAckCfg.Id, busyOffCfg.Id, "Next");
            AddEdge(busyOffCfg.Id, completeOffCfg.Id, "Next");
            AddEdge(completeOffCfg.Id, repeatCfg.Id, "Next");
            AddEdge(repeatCfg.Id, recipeChangeCfg.Id, "Repeat");

            SequenceName = "Process Sequence";
            StatusMessage = string.IsNullOrWhiteSpace(ResetSignalAddress)
                ? $"디폴트 프로세스 시퀀스가 생성되었습니다. (카메라 {AvailableCameraIds.Count}대) — Reset 신호 주소를 상단 툴바에서 설정하세요."
                : $"디폴트 프로세스 시퀀스가 생성되었습니다. (카메라 {AvailableCameraIds.Count}대, Reset: {ResetSignalAddress})";
        }

        // ====================================================================
        // System-level Save/Load (AppData — 머신 단위 단일 시퀀스)
        // ====================================================================

        /// <summary>시스템 시퀀스 저장 (AppData process_sequence.json)</summary>
        [RelayCommand]
        private void SaveSequence()
        {
            if (Nodes.Count == 0)
            {
                _dialogService.ShowWarning("저장할 시퀀스가 없습니다.", "저장");
                return;
            }

            // 외부 파일로 작업 중이면 해당 파일에 저장
            if (!string.IsNullOrEmpty(CurrentFilePath))
            {
                WriteSequenceFile(CurrentFilePath);
                return;
            }

            // 시스템 시퀀스로 저장
            WriteSequenceFile(SystemSequencePath);
            CurrentFilePath = null;
        }

        /// <summary>시스템 시퀀스 자동 로드 (에디터 시작 시 호출)</summary>
        private void LoadSystemSequence()
        {
            try
            {
                if (!File.Exists(SystemSequencePath)) return;

                var json = File.ReadAllText(SystemSequencePath);
                var config = JsonSerializer.Deserialize<SequenceConfig>(json, _jsonOptions);
                if (config == null) return;

                FromConfig(config);
                CurrentFilePath = null;
                StatusMessage = $"시스템 시퀀스 로드됨: {config.Name}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SequenceEditor] System sequence load error: {ex}");
            }
        }

        // ====================================================================
        // File Export/Import (외부 파일 — 백업/이동용)
        // ====================================================================

        /// <summary>다른 이름으로 저장 (파일 내보내기)</summary>
        [RelayCommand]
        private void ExportSequenceToFile()
        {
            if (Nodes.Count == 0)
            {
                _dialogService.ShowWarning("저장할 시퀀스가 없습니다.", "내보내기");
                return;
            }

            var path = _dialogService.ShowSaveFileDialog(SequenceFileFilter, ".seq.json", SequenceName);
            if (path == null) return;

            WriteSequenceFile(path);
            CurrentFilePath = path;
        }

        /// <summary>파일에서 불러오기 (가져오기)</summary>
        [RelayCommand]
        private void ImportSequenceFromFile()
        {
            if (Nodes.Count > 0)
            {
                if (!_dialogService.ShowConfirmation(
                    "현재 시퀀스를 닫고 파일에서 불러오시겠습니까?", "파일 가져오기"))
                    return;
            }

            var path = _dialogService.ShowOpenFileDialog("시퀀스 파일 열기", SequenceFileFilter);
            if (path == null) return;

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<SequenceConfig>(json, _jsonOptions);
                if (config == null)
                {
                    _dialogService.ShowWarning("유효하지 않은 시퀀스 파일입니다.", "로드 실패");
                    return;
                }

                FromConfig(config);
                CurrentFilePath = path;
                StatusMessage = $"시퀀스 가져옴: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"시퀀스 로드 실패: {ex.Message}", "로드 오류");
            }
        }

        private void WriteSequenceFile(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var config = ToConfig();
                var json = JsonSerializer.Serialize(config, _jsonOptions);
                File.WriteAllText(path, json);
                StatusMessage = $"시퀀스 저장됨: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"시퀀스 저장 실패: {ex.Message}", "저장 오류");
            }
        }

        /// <summary>현재 에디터 상태를 SequenceConfig로 변환</summary>
        public SequenceConfig ToConfig()
        {
            // 노드 동기화
            foreach (var node in Nodes)
                node.SyncToConfig();

            var config = new SequenceConfig
            {
                Name = SequenceName,
                ResetSignalAddress = string.IsNullOrWhiteSpace(ResetSignalAddress) ? null : ResetSignalAddress.Trim(),
                ResetSignalCheckMode = ResetSignalCheckMode,
                ResetSignalCompareValue = ResetSignalCompareValue,
                Nodes = Nodes.Select(n => n.Config).ToList(),
                Edges = Edges.Select(e => new SequenceEdgeConfig
                {
                    Id = e.Id,
                    SourceNodeId = e.SourceNode?.Id ?? string.Empty,
                    TargetNodeId = e.TargetNode?.Id ?? string.Empty,
                    Label = e.Label
                }).ToList()
            };
            return config;
        }

        /// <summary>SequenceConfig에서 에디터 상태 복원</summary>
        public void FromConfig(SequenceConfig config)
        {
            Nodes.Clear();
            Edges.Clear();
            SelectedNode = null;

            SequenceName = config.Name;
            ResetSignalAddress = config.ResetSignalAddress ?? string.Empty;
            ResetSignalCheckMode = config.ResetSignalCheckMode;
            ResetSignalCompareValue = config.ResetSignalCompareValue;

            var nodeDict = new System.Collections.Generic.Dictionary<string, SequenceNodeItem>();
            foreach (var nodeConfig in config.Nodes)
            {
                var item = new SequenceNodeItem(nodeConfig);
                Nodes.Add(item);
                nodeDict[item.Id] = item;
            }

            foreach (var edgeConfig in config.Edges)
            {
                nodeDict.TryGetValue(edgeConfig.SourceNodeId, out var source);
                nodeDict.TryGetValue(edgeConfig.TargetNodeId, out var target);
                Edges.Add(new SequenceEdgeItem(edgeConfig, source, target));
            }
        }

        // ====================================================================
        // PLC I/O Monitor
        // ====================================================================

        [RelayCommand]
        private void ToggleMonitorPanel()
        {
            IsMonitorPanelVisible = !IsMonitorPanelVisible;
            if (IsMonitorPanelVisible)
                CollectMonitorAddresses();
        }

        /// <summary>모니터링 시작/중지 토글 (수동)</summary>
        [RelayCommand]
        private void ToggleMonitoring()
        {
            if (!IsPlcConnected)
            {
                StatusMessage = "PLC가 연결되어 있지 않습니다.";
                return;
            }

            if (IsPlcMonitoring)
            {
                StopMonitor();
                StatusMessage = "PLC 모니터링 중지됨";
            }
            else
            {
                CollectMonitorAddresses();
                StartMonitor();
                StatusMessage = "PLC 모니터링 시작 (1초 주기)";
            }
        }

        [RelayCommand]
        private async Task ConnectPlcAsync()
        {
            if (IsPlcConnected)
            {
                await DisconnectPlcAsync();
                return;
            }

            try
            {
                PlcConnectionStatus = "연결 중...";
                StatusMessage = "PLC 연결 중...";

                _plcConfig = PlcConfigLoader.LoadFromAppData();
                if (_plcConfig == null)
                {
                    PlcConnectionStatus = "설정 없음";
                    _dialogService.ShowWarning(
                        "system_config.json에 PLC 설정이 없거나 PLC 벤더가 None입니다.\nBODA Setup에서 PLC를 설정해 주세요.",
                        "PLC 연결");
                    return;
                }

                _plcConnection = PlcConnectionFactory.Create(_plcConfig);
                var connected = await _plcConnection.ConnectAsync(_plcConfig);

                if (!connected)
                {
                    PlcConnectionStatus = "연결 실패";
                    StatusMessage = "PLC 연결 실패";
                    _plcConnection.Dispose();
                    _plcConnection = null;
                    return;
                }

                IsPlcConnected = true;
                PlcConnectionStatus = $"연결됨 ({_plcConfig.Vendor} {_plcConfig.IpAddress})";
                StatusMessage = $"PLC 연결 성공: {_plcConfig.Vendor} {_plcConfig.IpAddress}";

                CollectMonitorAddresses();
                IsMonitorPanelVisible = true;
                // 모니터링은 자동 시작하지 않음 — 사용자가 명시적으로 시작해야 함
            }
            catch (Exception ex)
            {
                PlcConnectionStatus = "연결 오류";
                StatusMessage = $"PLC 연결 오류: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[SequenceEditor] PLC connect error: {ex}");

                _plcConnection?.Dispose();
                _plcConnection = null;
            }
        }

        /// <summary>PLC 연결 해제 및 모니터링 중지</summary>
        [RelayCommand]
        public async Task DisconnectPlcAsync()
        {
            StopMonitor();

            if (_plcConnection != null)
            {
                try
                {
                    await _plcConnection.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SequenceEditor] PLC disconnect error: {ex}");
                }
                finally
                {
                    _plcConnection.Dispose();
                    _plcConnection = null;
                }
            }

            IsPlcConnected = false;
            IsPlcMonitoring = false;
            PlcConnectionStatus = "미연결";
            StatusMessage = "PLC 연결 해제됨";

            // 노드 조건 상태 초기화
            foreach (var node in Nodes)
                node.ConditionStatus = null;

            foreach (var item in MonitorItems)
            {
                item.CurrentValue = "---";
                item.ConditionMet = null;
                item.ErrorMessage = null;
            }
        }

        /// <summary>노드 속성이 변경되었을 때 MonitorItems를 증분 갱신 (매 모니터 주기 호출)</summary>
        private string _lastMonitorFingerprint = string.Empty;

        private void RefreshMonitorAddresses()
        {
            if (_plcConfig == null) return;

            // 주소 + DataType + CheckMode 조합으로 fingerprint 생성
            var parts = new List<string>();
            foreach (var node in Nodes)
            {
                var cfg = node.Config;
                if (string.IsNullOrWhiteSpace(cfg.PlcAddress)) continue;

                if (cfg.NodeType == SequenceNodeType.InputCheck)
                    parts.Add($"{cfg.PlcAddress.Trim()}|I|{cfg.CheckMode}");
                else if (cfg.NodeType == SequenceNodeType.OutputAction)
                    parts.Add($"{cfg.PlcAddress.Trim()}|O|{cfg.OutputDataType}");
            }
            parts.Sort();
            var fingerprint = string.Join(";", parts);

            if (fingerprint == _lastMonitorFingerprint) return;

            _lastMonitorFingerprint = fingerprint;
            CollectMonitorAddresses();
        }

        /// <summary>Nodes에서 PlcAddress 있는 InputCheck/OutputAction을 스캔하여 MonitorItems 생성</summary>
        private void CollectMonitorAddresses()
        {
            MonitorItems.Clear();

            if (_plcConfig == null) return;

            // 주소별 그룹핑 (같은 주소를 여러 노드가 참조할 수 있음)
            var addressMap = new Dictionary<string, (PlcAddress parsed, PlcDataType dataType, PlcIoDirection dir, List<string> nodeNames)>();

            foreach (var node in Nodes)
            {
                var cfg = node.Config;
                if (string.IsNullOrWhiteSpace(cfg.PlcAddress)) continue;

                if (cfg.NodeType == SequenceNodeType.InputCheck)
                {
                    var addrStr = cfg.PlcAddress.Trim();
                    var dataType = cfg.CheckMode is InputCheckMode.BitOn or InputCheckMode.BitOff
                        ? PlcDataType.Bit : PlcDataType.Int16;

                    if (!addressMap.TryGetValue(addrStr, out var entry))
                    {
                        try
                        {
                            var parsed = PlcAddress.Parse(addrStr, _plcConfig.Vendor);
                            entry = (parsed, dataType, PlcIoDirection.Input, new List<string>());
                            addressMap[addrStr] = entry;
                        }
                        catch { continue; }
                    }
                    entry.nodeNames.Add(node.Name);
                }
                else if (cfg.NodeType == SequenceNodeType.OutputAction)
                {
                    var addrStr = cfg.PlcAddress.Trim();

                    if (!addressMap.TryGetValue(addrStr, out var entry))
                    {
                        try
                        {
                            var parsed = PlcAddress.Parse(addrStr, _plcConfig.Vendor);
                            entry = (parsed, cfg.OutputDataType, PlcIoDirection.Output, new List<string>());
                            addressMap[addrStr] = entry;
                        }
                        catch { continue; }
                    }
                    entry.nodeNames.Add(node.Name);
                }
            }

            foreach (var kvp in addressMap)
            {
                var (parsed, dataType, dir, nodeNames) = kvp.Value;
                var item = new PlcMonitorItem(kvp.Key, parsed, dataType, dir)
                {
                    ReferencedNodes = string.Join(", ", nodeNames)
                };
                MonitorItems.Add(item);
            }
        }

        private void StartMonitor()
        {
            StopMonitor();
            _monitorCts = new CancellationTokenSource();
            IsPlcMonitoring = true;
            _ = MonitorLoopAsync(_monitorCts.Token);
        }

        private void StopMonitor()
        {
            if (_monitorCts != null)
            {
                _monitorCts.Cancel();
                _monitorCts.Dispose();
                _monitorCts = null;
            }
            IsPlcMonitoring = false;
        }

        /// <summary>PeriodicTimer로 PLC 주소를 주기적으로 읽어 UI 갱신 (수동 토글)</summary>
        private async Task MonitorLoopAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000));
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    if (_plcConnection == null || !_plcConnection.IsConnected) continue;

                    // 노드 속성 변경을 반영하기 위해 매 주기 주소 재수집
                    RefreshMonitorAddresses();

                    foreach (var item in MonitorItems)
                    {
                        try
                        {
                            string value;
                            switch (item.DataType)
                            {
                                case PlcDataType.Bit:
                                    var bitVal = await _plcConnection.ReadBitAsync(item.ParsedAddress);
                                    value = bitVal ? "ON" : "OFF";
                                    break;
                                case PlcDataType.Int16:
                                    var wordVal = await _plcConnection.ReadWordAsync(item.ParsedAddress);
                                    value = wordVal.ToString();
                                    break;
                                case PlcDataType.Int32:
                                    var dwordVal = await _plcConnection.ReadDWordAsync(item.ParsedAddress);
                                    value = dwordVal.ToString();
                                    break;
                                case PlcDataType.Float:
                                    var rawBits = await _plcConnection.ReadDWordAsync(item.ParsedAddress);
                                    var floatVal = BitConverter.Int32BitsToSingle(rawBits);
                                    value = floatVal.ToString("F3");
                                    break;
                                default:
                                    value = "---";
                                    break;
                            }

                            item.CurrentValue = value;
                            item.LastUpdated = DateTime.Now.ToString("HH:mm:ss");
                            item.ErrorMessage = null;
                        }
                        catch (Exception ex)
                        {
                            item.CurrentValue = "ERR";
                            item.ErrorMessage = ex.Message;
                        }
                    }

                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SequenceEditor] Monitor loop error: {ex}");
                IsPlcMonitoring = false;
            }
        }

        // ====================================================================
        // Sequence Test Run — 시퀀스 순차 실행 테스트
        // ====================================================================

        /// <summary>무부하 시퀀스 테스트 실행 — 실제 PLC 신호 송수신, 카메라 검사는 OK 시뮬레이션</summary>
        [RelayCommand]
        private async Task TestRunSequenceAsync()
        {
            if (_plcConnection == null || !IsPlcConnected)
            {
                StatusMessage = "PLC가 연결되어 있지 않습니다.";
                return;
            }

            var startNode = Nodes.FirstOrDefault(n => n.NodeType == SequenceNodeType.Start);
            if (startNode == null)
            {
                StatusMessage = "Start 노드가 없습니다.";
                return;
            }

            // 테스트 실행 중에는 수동 모니터링 중지 (요청 충돌 방지)
            var wasMonitoring = IsPlcMonitoring;
            if (wasMonitoring)
                StopMonitor();

            IsTestRunning = true;
            TestCycleCount = 0;
            _testRunCts = new CancellationTokenSource();
            var ct = _testRunCts.Token;

            // 모든 노드 상태 초기화
            foreach (var n in Nodes) { n.IsActive = false; n.ConditionStatus = null; }

            var nodeDict = Nodes.ToDictionary(n => n.Id);
            var currentNode = startNode;
            var repeatCounters = new Dictionary<string, int>();

            // Inspection 결과 추적 (SequenceEngine과 동일 로직)
            bool lastInspectionOk = true;
            var cameraResults = new Dictionary<string, bool>();

            try
            {
                while (currentNode != null && !ct.IsCancellationRequested)
                {
                    // 이전 노드 비활성, 현재 노드 활성
                    foreach (var n in Nodes) n.IsActive = false;
                    currentNode.IsActive = true;
                    ActiveNodeName = currentNode.Name;

                    string? nextNodeId = null;

                    switch (currentNode.NodeType)
                    {
                        case SequenceNodeType.Start:
                            StatusMessage = $"[무부하] {currentNode.Name} — 시퀀스 시작";
                            nextNodeId = currentNode.Config.NextNodeId;
                            break;

                        case SequenceNodeType.InputCheck:
                            nextNodeId = await ExecuteTestInputCheck(currentNode, ct);
                            break;

                        case SequenceNodeType.OutputAction:
                            await ExecuteTestOutputAction(currentNode);
                            nextNodeId = currentNode.Config.NextNodeId;
                            break;

                        case SequenceNodeType.Inspection:
                        {
                            var cameraId = currentNode.Config.CameraId ?? "(none)";
                            StatusMessage = $"[무부하] {currentNode.Name} — 검사 시뮬레이션 OK (Camera: {cameraId})";
                            currentNode.ConditionStatus = true;

                            // 무부하 모드: 검사 항상 OK
                            lastInspectionOk = true;
                            if (!string.IsNullOrEmpty(currentNode.Config.CameraId))
                                cameraResults[currentNode.Config.CameraId] = true;

                            await Task.Delay(200, ct);
                            nextNodeId = currentNode.Config.NextNodeId;
                            break;
                        }

                        case SequenceNodeType.Branch:
                        {
                            // SequenceEngine과 동일: BranchOnAllCameras 플래그에 따라 결과 결정
                            var cfg = currentNode.Config;
                            bool branchResult = cfg.BranchOnAllCameras
                                ? (cameraResults.Count == 0 || cameraResults.Values.All(v => v))
                                : lastInspectionOk;
                            string branchPath = branchResult ? "OK → True 경로" : "NG → False 경로";

                            StatusMessage = $"[무부하] {currentNode.Name} — 분기 판정: {branchPath}";
                            currentNode.ConditionStatus = branchResult;
                            await Task.Delay(200, ct);

                            nextNodeId = branchResult ? cfg.TrueBranchNodeId : cfg.FalseBranchNodeId;
                            break;
                        }

                        case SequenceNodeType.Delay:
                            var delayMs = currentNode.Config.DelayMs;
                            StatusMessage = $"[무부하] {currentNode.Name} — {delayMs}ms 대기";
                            await Task.Delay(Math.Max(0, delayMs), ct);
                            nextNodeId = currentNode.Config.NextNodeId;
                            break;

                        case SequenceNodeType.Repeat:
                        {
                            var repeatId = currentNode.Id;
                            if (!repeatCounters.ContainsKey(repeatId))
                                repeatCounters[repeatId] = 0;
                            repeatCounters[repeatId]++;

                            var maxCount = currentNode.Config.RepeatCount;
                            if (maxCount == -1 || repeatCounters[repeatId] <= maxCount)
                            {
                                // 무한 반복 시 사이클 카운트 업데이트
                                if (maxCount == -1)
                                    TestCycleCount = repeatCounters[repeatId];

                                StatusMessage = $"[무부하] {currentNode.Name} — 반복 {repeatCounters[repeatId]}회"
                                    + (maxCount == -1 ? " (무한)" : $" / {maxCount}");
                                nextNodeId = currentNode.Config.RepeatTargetNodeId;

                                // 반복 시 검사 결과 초기화 (새 사이클)
                                lastInspectionOk = true;
                                cameraResults.Clear();
                            }
                            else
                            {
                                StatusMessage = $"[무부하] {currentNode.Name} — 반복 완료 ({maxCount}회)";
                                nextNodeId = currentNode.Config.NextNodeId;
                            }
                            break;
                        }

                        case SequenceNodeType.RecipeChange:
                        {
                            var sigAddr = currentNode.Config.RecipeSignalAddress ?? "(none)";
                            var idxAddr = currentNode.Config.RecipeIndexAddress ?? "(none)";
                            StatusMessage = $"[무부하] {currentNode.Name} — Signal({sigAddr}) 확인 → Index({idxAddr}) 읽기 시뮬레이션";
                            await Task.Delay(200, ct);
                            nextNodeId = currentNode.Config.NextNodeId;
                            break;
                        }

                        case SequenceNodeType.StepChange:
                        {
                            var sigAddr = currentNode.Config.StepSignalAddress ?? "(none)";
                            var idxAddr = currentNode.Config.StepIndexAddress ?? "(none)";
                            StatusMessage = $"[무부하] {currentNode.Name} — Signal({sigAddr}) 확인 → Step Index({idxAddr}) → 스텝 0 사용";
                            await Task.Delay(200, ct);
                            nextNodeId = currentNode.Config.NextNodeId;
                            break;
                        }

                        case SequenceNodeType.End:
                            StatusMessage = "[무부하] 시퀀스 완료";
                            currentNode.IsActive = false;
                            currentNode = null;
                            continue;
                    }

                    // 노드 간 시각적 피드백 딜레이
                    await Task.Delay(100, ct);

                    // 다음 노드 이동
                    if (nextNodeId != null && nodeDict.TryGetValue(nextNodeId, out var next))
                        currentNode = next;
                    else
                        currentNode = null;
                }

                if (ct.IsCancellationRequested)
                    StatusMessage = $"[무부하] 테스트 중지됨 (총 {TestCycleCount}회 수행)";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = $"[무부하] 테스트 중지됨 (총 {TestCycleCount}회 수행)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"[무부하 오류] {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[SequenceEditor] Test run error: {ex}");
            }
            finally
            {
                foreach (var n in Nodes) { n.IsActive = false; n.ConditionStatus = null; }
                IsTestRunning = false;
                ActiveNodeName = null;

                // 테스트 전 모니터링이 활성이었으면 복원
                if (wasMonitoring && IsPlcConnected)
                    StartMonitor();
            }
        }

        /// <summary>테스트 실행 중지</summary>
        [RelayCommand]
        private void StopTestRun()
        {
            _testRunCts?.Cancel();
        }

        /// <summary>InputCheck 노드 — 조건 충족까지 PLC 폴링</summary>
        private async Task<string?> ExecuteTestInputCheck(SequenceNodeItem node, CancellationToken ct)
        {
            var cfg = node.Config;
            if (string.IsNullOrWhiteSpace(cfg.PlcAddress) || _plcConfig == null || _plcConnection == null)
            {
                StatusMessage = $"[무부하] {node.Name} — PLC 주소 미설정, 건너뜀";
                await Task.Delay(200, ct);
                return cfg.NextNodeId;
            }

            var addr = PlcAddress.Parse(cfg.PlcAddress.Trim(), _plcConfig.Vendor);
            var modeText = cfg.CheckMode switch
            {
                InputCheckMode.BitOn => $"{cfg.PlcAddress} = ON",
                InputCheckMode.BitOff => $"{cfg.PlcAddress} = OFF",
                InputCheckMode.WordEquals => $"{cfg.PlcAddress} == {cfg.CompareValue ?? 0}",
                InputCheckMode.WordGreaterThan => $"{cfg.PlcAddress} > {cfg.CompareValue ?? 0}",
                InputCheckMode.WordLessThan => $"{cfg.PlcAddress} < {cfg.CompareValue ?? 0}",
                _ => cfg.PlcAddress
            };
            var startTime = DateTime.UtcNow;

            while (!ct.IsCancellationRequested)
            {
                // 타임아웃 체크
                if (cfg.TimeoutMs > 0)
                {
                    var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    if (elapsed >= cfg.TimeoutMs)
                    {
                        node.ConditionStatus = false;
                        StatusMessage = $"[무부하] {node.Name} — 타임아웃 ({cfg.TimeoutMs}ms)";
                        return cfg.NextNodeId;
                    }
                }

                // 현재값 읽기
                string currentVal;
                if (cfg.CheckMode is InputCheckMode.BitOn or InputCheckMode.BitOff)
                {
                    var bit = await _plcConnection.ReadBitAsync(addr);
                    currentVal = bit ? "ON" : "OFF";
                }
                else
                {
                    var word = await _plcConnection.ReadWordAsync(addr);
                    currentVal = word.ToString();
                }

                // 조건 평가
                bool met = cfg.CheckMode switch
                {
                    InputCheckMode.BitOn => currentVal == "ON",
                    InputCheckMode.BitOff => currentVal == "OFF",
                    InputCheckMode.WordEquals when int.TryParse(currentVal, out var v) =>
                        v == (cfg.CompareValue ?? 0),
                    InputCheckMode.WordGreaterThan when int.TryParse(currentVal, out var v) =>
                        v > (cfg.CompareValue ?? 0),
                    InputCheckMode.WordLessThan when int.TryParse(currentVal, out var v) =>
                        v < (cfg.CompareValue ?? 0),
                    _ => false
                };

                node.ConditionStatus = met;
                StatusMessage = $"[무부하] {node.Name} — 신호 대기 ({modeText}, 현재: {currentVal})";

                if (met)
                {
                    StatusMessage = $"[무부하] {node.Name} — 조건 충족 ({currentVal})";
                    return cfg.NextNodeId;
                }

                await Task.Delay(100, ct);
            }

            return null; // cancelled
        }

        /// <summary>OutputAction 노드 — PLC에 값 쓰기</summary>
        private async Task ExecuteTestOutputAction(SequenceNodeItem node)
        {
            var cfg = node.Config;
            if (string.IsNullOrWhiteSpace(cfg.PlcAddress) || _plcConfig == null || _plcConnection == null)
            {
                StatusMessage = $"[무부하] {node.Name} — PLC 주소 미설정, 건너뜀";
                return;
            }

            var addr = PlcAddress.Parse(cfg.PlcAddress.Trim(), _plcConfig.Vendor);

            switch (cfg.OutputDataType)
            {
                case PlcDataType.Bit:
                    await _plcConnection.WriteBitAsync(addr, cfg.BitValue ?? false);
                    StatusMessage = $"[무부하] {node.Name} — {cfg.PlcAddress} ← {(cfg.BitValue == true ? "ON" : "OFF")}";
                    break;
                case PlcDataType.Int16:
                    await _plcConnection.WriteWordAsync(addr, (short)(cfg.WordValue ?? 0));
                    StatusMessage = $"[무부하] {node.Name} — {cfg.PlcAddress} ← {cfg.WordValue ?? 0}";
                    break;
                case PlcDataType.Int32:
                    await _plcConnection.WriteDWordAsync(addr, cfg.WordValue ?? 0);
                    StatusMessage = $"[무부하] {node.Name} — {cfg.PlcAddress} ← {cfg.WordValue ?? 0}";
                    break;
                case PlcDataType.Float:
                    var bits = BitConverter.SingleToInt32Bits(cfg.FloatValue ?? 0f);
                    await _plcConnection.WriteDWordAsync(addr, bits);
                    StatusMessage = $"[무부하] {node.Name} — {cfg.PlcAddress} ← {cfg.FloatValue ?? 0f:F3}";
                    break;
            }

            node.ConditionStatus = true;
        }

        // ====================================================================
        // Test Write / Test Read — 개별 노드 단건 테스트
        // ====================================================================

        /// <summary>선택된 OutputAction 노드의 설정값을 PLC에 1회 쓰기 (테스트용)</summary>
        [RelayCommand]
        private async Task TestWriteOutputAsync()
        {
            if (_plcConnection == null || !_plcConnection.IsConnected)
            {
                StatusMessage = "PLC가 연결되어 있지 않습니다.";
                return;
            }

            var node = SelectedNode;
            if (node == null || node.NodeType != SequenceNodeType.OutputAction)
            {
                StatusMessage = "OutputAction 노드를 선택해주세요.";
                return;
            }

            var cfg = node.Config;
            if (string.IsNullOrWhiteSpace(cfg.PlcAddress) || _plcConfig == null)
            {
                StatusMessage = "PLC 주소가 설정되지 않았습니다.";
                return;
            }

            try
            {
                var addr = PlcAddress.Parse(cfg.PlcAddress.Trim(), _plcConfig.Vendor);

                switch (cfg.OutputDataType)
                {
                    case PlcDataType.Bit:
                        await _plcConnection.WriteBitAsync(addr, cfg.BitValue ?? false);
                        StatusMessage = $"[테스트 출력] {cfg.PlcAddress} ← {(cfg.BitValue == true ? "ON" : "OFF")}";
                        break;
                    case PlcDataType.Int16:
                        await _plcConnection.WriteWordAsync(addr, (short)(cfg.WordValue ?? 0));
                        StatusMessage = $"[테스트 출력] {cfg.PlcAddress} ← {cfg.WordValue ?? 0}";
                        break;
                    case PlcDataType.Int32:
                        await _plcConnection.WriteDWordAsync(addr, cfg.WordValue ?? 0);
                        StatusMessage = $"[테스트 출력] {cfg.PlcAddress} ← {cfg.WordValue ?? 0}";
                        break;
                    case PlcDataType.Float:
                        var bits = BitConverter.SingleToInt32Bits(cfg.FloatValue ?? 0f);
                        await _plcConnection.WriteDWordAsync(addr, bits);
                        StatusMessage = $"[테스트 출력] {cfg.PlcAddress} ← {cfg.FloatValue ?? 0f:F3}";
                        break;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"[테스트 출력 실패] {ex.Message}";
            }
        }

        /// <summary>선택된 InputCheck 노드의 PLC 주소에서 현재값 1회 읽기 (테스트용)</summary>
        [RelayCommand]
        private async Task TestReadInputAsync()
        {
            if (_plcConnection == null || !_plcConnection.IsConnected)
            {
                StatusMessage = "PLC가 연결되어 있지 않습니다.";
                return;
            }

            var node = SelectedNode;
            if (node == null || node.NodeType != SequenceNodeType.InputCheck)
            {
                StatusMessage = "InputCheck 노드를 선택해주세요.";
                return;
            }

            var cfg = node.Config;
            if (string.IsNullOrWhiteSpace(cfg.PlcAddress) || _plcConfig == null)
            {
                StatusMessage = "PLC 주소가 설정되지 않았습니다.";
                return;
            }

            try
            {
                var addr = PlcAddress.Parse(cfg.PlcAddress.Trim(), _plcConfig.Vendor);
                string value;

                if (cfg.CheckMode is InputCheckMode.BitOn or InputCheckMode.BitOff)
                {
                    var bit = await _plcConnection.ReadBitAsync(addr);
                    value = bit ? "ON" : "OFF";
                }
                else
                {
                    var word = await _plcConnection.ReadWordAsync(addr);
                    value = word.ToString();
                }

                StatusMessage = $"[테스트 읽기] {cfg.PlcAddress} = {value}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"[테스트 읽기 실패] {ex.Message}";
            }
        }

    }

    /// <summary>
    /// 노드 팔레트 아이템
    /// </summary>
    public class NodePaletteItem
    {
        public SequenceNodeType NodeType { get; }
        public string DisplayName { get; }
        public string Color { get; }

        public NodePaletteItem(SequenceNodeType nodeType, string displayName, string color)
        {
            NodeType = nodeType;
            DisplayName = displayName;
            Color = color;
        }
    }
}
