using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.WeldTeach.Helpers;
using VMS.WeldTeach.Interfaces;
using VMS.WeldTeach.Models;
using VMS.WeldTeach.Services;

namespace VMS.WeldTeach.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ICadKernelService _cadKernel;
    private readonly IDialogService _dialogService;
    private readonly EdgeChainService _chainService;
    private readonly TorchPoseService _poseService;

    public MainViewModel(ICadKernelService cadKernel, IDialogService dialogService,
        EdgeChainService chainService, TorchPoseService poseService)
    {
        _cadKernel = cadKernel;
        _dialogService = dialogService;
        _chainService = chainService;
        _poseService = poseService;
    }

    [ObservableProperty]
    private CadModelData? _model;

    [ObservableProperty]
    private string _statusText = "STEP 파일을 열거나 샘플 시편을 생성하세요.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _hoveredEdgeId;

    [ObservableProperty]
    private WeldingPathContour? _selectedContour;

    [ObservableProperty]
    private double _pickThreshold = 1.5;

    [ObservableProperty]
    private double _chainAngleToleranceDeg = 15.0;

    public ObservableCollection<TorchPose> Poses { get; } = new();

    /// <summary>뷰(뷰포트 렌더러)가 모델/하이라이트 변경을 반영하도록 하는 이벤트.</summary>
    public event EventHandler? ModelChanged;
    public event EventHandler? HighlightChanged;

    [RelayCommand]
    private async Task OpenStepAsync()
    {
        var path = _dialogService.ShowOpenStepDialog();
        if (path == null) return;
        await LoadAsync(path);
    }

    [RelayCommand]
    private async Task GenerateSampleAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "weldteach_sample_tjoint.step");
        IsBusy = true;
        StatusText = "샘플 시편(STEP) 생성 중...";
        try
        {
            await Task.Run(() => _cadKernel.GenerateSampleStep(path));
        }
        finally { IsBusy = false; }
        await LoadAsync(path);
    }

    /// <summary>진단 모드(--open) 에서 GUI 와 동일한 로드 경로를 태우기 위한 공개 진입점.</summary>
    public Task LoadForDiagnosticsAsync(string path) => LoadAsync(path);

    private async Task LoadAsync(string path)
    {
        IsBusy = true;
        StatusText = $"로드 중: {Path.GetFileName(path)}";
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var model = await Task.Run(() => _cadKernel.LoadStep(path));
            sw.Stop();
            Model = model;
            SelectedContour = null;
            Poses.Clear();
            StatusText = $"{Path.GetFileName(path)} — 솔리드 {model.SolidCount} / 면 {model.FaceCount} / " +
                         $"엣지 {model.Edges.Count} ({sw.ElapsedMilliseconds} ms)";
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = $"로드 실패: {ex.Message} — 상세: %TEMP%\\weldteach.log";
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "weldteach.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] LoadStep 실패 path={path}{Environment.NewLine}{ex}{Environment.NewLine}");
            }
            catch { /* 로그 실패는 무시 */ }
        }
        finally { IsBusy = false; }
    }

    /// <summary>뷰포트 마우스 이동 — Ray 기반 엣지 호버링 (명세서 Step 1).</summary>
    public void OnViewportHover(Point3D rayOrigin, Vector3D rayDirection)
    {
        if (Model == null) return;
        var hit = RayCaster.PickEdge(rayOrigin, rayDirection, Model.Edges, PickThreshold);
        int newId = hit?.EdgeId ?? 0;
        if (newId != HoveredEdgeId)
        {
            HoveredEdgeId = newId;
            HighlightChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>뷰포트 클릭 — 엣지 선택 후 체이닝 + 토치 포즈 계산 (명세서 Step 2·3).</summary>
    public void OnViewportClick(Point3D rayOrigin, Vector3D rayDirection)
    {
        if (Model == null) return;
        var hit = RayCaster.PickEdge(rayOrigin, rayDirection, Model.Edges, PickThreshold);
        if (hit == null)
        {
            SelectedContour = null;
            Poses.Clear();
            HighlightChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        SelectEdge(hit.EdgeId);
    }

    /// <summary>엣지 Id 로 직접 선택 — 클릭과 동일 경로. 진단 모드(--pick)에서도 사용.</summary>
    public void SelectEdge(int edgeId)
    {
        if (Model == null) return;
        _chainService.AngleToleranceDeg = ChainAngleToleranceDeg;
        var contour = _chainService.BuildChain(Model, edgeId);
        var poses = _poseService.ComputePoses(contour, Model);

        SelectedContour = contour;
        Poses.Clear();
        foreach (var p in poses) Poses.Add(p);

        StatusText = $"{contour.PathId}: 엣지 {contour.EdgeIds.Count}개 체이닝, " +
                     $"길이 {contour.TotalLength:F1} mm, 포즈 {poses.Count}개";
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ExportPath()
    {
        if (SelectedContour == null || Poses.Count == 0)
        {
            _dialogService.ShowMessage("내보낼 경로가 없습니다. 먼저 용접 엣지를 클릭해 선택하세요.", "내보내기");
            return;
        }
        var path = _dialogService.ShowSaveJsonDialog($"{SelectedContour.PathId}.json");
        if (path == null) return;
        _poseService.ExportJson(path, SelectedContour, Poses.ToList());
        StatusText = $"내보내기 완료: {path}";
    }
}
