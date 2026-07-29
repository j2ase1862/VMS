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

    /// <summary>선택된 용접 경로들 — 목록 순서가 곧 용접 순서다.</summary>
    public ObservableCollection<WeldingPathContour> Contours { get; } = new();

    [ObservableProperty]
    private WeldingPathContour? _selectedContour;

    [ObservableProperty]
    private double _pickThreshold = 1.5;

    [ObservableProperty]
    private double _chainAngleToleranceDeg = 15.0;

    /// <summary>포즈(웨이포인트) 간격 mm — 변경 시 모든 경로의 포즈를 즉시 재계산.</summary>
    [ObservableProperty]
    private double _poseSpacingMm = 1.5;

    partial void OnPoseSpacingMmChanged(double value) => RecomputeAllPoses();

    private void RecomputeAllPoses()
    {
        if (Model == null || Contours.Count == 0) return;
        foreach (var c in Contours)
            c.Poses = _poseService.ComputePoses(c, Model, PoseSpacingMm);

        // 목록 요약(포즈 수)과 포즈 표 갱신
        System.Windows.Data.CollectionViewSource.GetDefaultView(Contours)?.Refresh();
        var sel = SelectedContour;
        Poses.Clear();
        if (sel != null)
            foreach (var p in sel.Poses) Poses.Add(p);

        StatusText = $"포즈 간격 {PoseSpacingMm:F1} mm 적용 — 경로 {Contours.Count}개, " +
                     $"총 포즈 {Contours.Sum(c => c.Poses.Count)}개";
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    public ObservableCollection<TorchPose> Poses { get; } = new();

    partial void OnSelectedContourChanged(WeldingPathContour? value)
    {
        Poses.Clear();
        if (value != null)
            foreach (var p in value.Poses) Poses.Add(p);
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

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
            Contours.Clear();
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

    /// <summary>
    /// 뷰포트 클릭 — 다중 경로 순차 선택 (명세서 Step 2·3).
    /// 새 엣지 클릭 → 체이닝해 경로 목록에 추가 (목록 순서 = 용접 순서).
    /// 이미 선택된 경로의 엣지를 다시 클릭 → 그 경로를 목록에서 제거 (토글).
    /// 빈 공간 클릭 → 선택 강조만 해제 (목록 유지).
    /// </summary>
    public void OnViewportClick(Point3D rayOrigin, Vector3D rayDirection)
    {
        if (Model == null) return;
        var hit = RayCaster.PickEdge(rayOrigin, rayDirection, Model.Edges, PickThreshold);
        if (hit == null)
        {
            SelectedContour = null;
            return;
        }

        var owner = Contours.FirstOrDefault(c => c.EdgeIds.Contains(hit.EdgeId));
        if (owner != null)
        {
            RemoveContour(owner);
            return;
        }
        SelectEdge(hit.EdgeId);
    }

    /// <summary>엣지 Id 로 경로 추가 — 클릭과 동일 경로. 진단 모드(--pick)에서도 사용.</summary>
    public void SelectEdge(int edgeId)
    {
        if (Model == null) return;
        _chainService.AngleToleranceDeg = ChainAngleToleranceDeg;
        var contour = _chainService.BuildChain(Model, edgeId);

        // 새 경로가 기존 경로와 엣지를 공유하면 중복 — 추가하지 않고 기존 경로를 선택
        var overlap = Contours.FirstOrDefault(c => c.EdgeIds.Intersect(contour.EdgeIds).Any());
        if (overlap != null)
        {
            SelectedContour = overlap;
            StatusText = $"{overlap.PathId} 는 이미 선택된 경로입니다 (같은 경로의 엣지를 다시 클릭하면 해제).";
            return;
        }

        contour.Poses = _poseService.ComputePoses(contour, Model, PoseSpacingMm);
        Contours.Add(contour);
        SelectedContour = contour;

        StatusText = $"[{Contours.Count}번] {contour.PathId} 추가: 엣지 {contour.EdgeIds.Count}개, " +
                     $"길이 {contour.TotalLength:F1} mm, 포즈 {contour.Poses.Count}개 — 총 {Contours.Count}개 경로";
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveContour(WeldingPathContour? contour)
    {
        if (contour == null) return;
        Contours.Remove(contour);
        if (SelectedContour == contour) SelectedContour = Contours.LastOrDefault();
        StatusText = $"{contour.PathId} 제거 — 남은 경로 {Contours.Count}개";
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearContours()
    {
        Contours.Clear();
        SelectedContour = null;
        StatusText = "모든 경로를 지웠습니다.";
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ExportPath()
    {
        if (Contours.Count == 0)
        {
            _dialogService.ShowMessage("내보낼 경로가 없습니다. 먼저 용접 엣지를 클릭해 선택하세요.", "내보내기");
            return;
        }
        var path = _dialogService.ShowSaveJsonDialog("weld_paths.json");
        if (path == null) return;
        _poseService.ExportJson(path, Contours.ToList());
        StatusText = $"내보내기 완료: 경로 {Contours.Count}개 → {path}";
    }
}
