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
    private readonly PointCloudService _cloudService;
    private readonly IcpService _icpService;
    private readonly CloudPreprocessService _prepService;

    public MainViewModel(ICadKernelService cadKernel, IDialogService dialogService,
        EdgeChainService chainService, TorchPoseService poseService,
        PointCloudService cloudService, IcpService icpService,
        CloudPreprocessService prepService)
    {
        _cadKernel = cadKernel;
        _dialogService = dialogService;
        _chainService = chainService;
        _poseService = poseService;
        _cloudService = cloudService;
        _icpService = icpService;
        _prepService = prepService;
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

    /// <summary>기본 포즈 간격 mm — 새로 추가되는 경로에 적용된다 (기존 경로는 [전체 적용]으로).</summary>
    [ObservableProperty]
    private double _poseSpacingMm = 1.5;

    /// <summary>곡률 적응 간격 기본값 — 새 경로에 적용. 켜면 간격은 최대치가 되고 곡선은 자동으로 촘촘해진다.</summary>
    [ObservableProperty]
    private bool _adaptiveSampling;

    /// <summary>선택된 경로의 개별 포즈 간격 mm — 편집 시 그 경로만 재계산 (직선 넓게 / 곡선 좁게).</summary>
    [ObservableProperty]
    private double _selectedPathSpacingMm = 1.5;

    /// <summary>선택된 경로의 곡률 적응 여부 — 변경 시 그 경로만 재계산.</summary>
    [ObservableProperty]
    private bool _selectedPathAdaptive;

    /// <summary>전체 보기 — 모든 경로의 포즈 점·토치 화살선을 동시에 표시.</summary>
    [ObservableProperty]
    private bool _showAllPaths;

    partial void OnShowAllPathsChanged(bool value) => HighlightChanged?.Invoke(this, EventArgs.Empty);

    // ---- 점군 정합 (명세서 Step 3-2: ICP → T_align) ----

    private List<Point3D>? _scanCloud;          // 스캔(로봇) 좌표계 원본
    // ICP 대상 CAD 표면 샘플 + 원본 삼각형 (점-표면 RMSE 용, 모델당 1회)
    private (List<Point3D> Points, List<int> TriIndex, List<(Point3D A, Point3D B, Point3D C)> Triangles)? _cadSamples;
    private Matrix3D? _tAlign;                  // CAD → 스캔 변환

    /// <summary>정합 완료 여부 — 내보내기 프레임 결정.</summary>
    public Matrix3D? TAlign => _tAlign;

    [ObservableProperty]
    private string _registrationStatus = "점군 없음";

    /// <summary>뷰포트 표시용 점군 — 정합 후에는 CAD 좌표계로 옮겨 모델 위에 겹쳐 보인다.</summary>
    public IReadOnlyList<Point3D> GetDisplayCloud(int maxPoints = 40000)
    {
        if (_scanCloud == null) return Array.Empty<Point3D>();
        var src = _scanCloud;
        Matrix3D? inv = null;
        if (_tAlign.HasValue)
        {
            var m = _tAlign.Value;
            if (m.HasInverse) { m.Invert(); inv = m; }
        }
        int stride = Math.Max(1, src.Count / maxPoints);
        var outPts = new List<Point3D>(Math.Min(src.Count, maxPoints) + 1);
        for (int i = 0; i < src.Count; i += stride)
            outPts.Add(inv.HasValue ? inv.Value.Transform(src[i]) : src[i]);
        return outPts;
    }

    [RelayCommand]
    private async Task OpenCloudAsync()
    {
        var path = _dialogService.ShowOpenCloudDialog();
        if (path == null) return;
        IsBusy = true;
        try
        {
            var cloud = await Task.Run(() => _cloudService.LoadCloud(path));
            SetScanCloud(cloud, $"점군 로드: {Path.GetFileName(path)} — {cloud.Count:N0}점 (미정합)");
        }
        catch (Exception ex)
        {
            StatusText = $"점군 로드 실패: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>합성 점군 생성 — 실카메라 없이 정합 흐름을 시험 (기지 오프셋 + 0.05mm 노이즈).</summary>
    [RelayCommand]
    private void GenerateSampleCloud()
    {
        if (Model == null)
        {
            _dialogService.ShowMessage("먼저 STEP 을 로드하세요.", "합성 점군");
            return;
        }
        var (cloud, _) = PointCloudService.GenerateSyntheticScan(Model, 0.05, keepAboveZ: null);
        SetScanCloud(cloud, $"합성 점군 생성 — {cloud.Count:N0}점, 오프셋 (12,-7,3)mm + Z10°/X5° (미정합)");
    }

    /// <summary>현재 점군(스캔 좌표계 원본)을 .vpc 로 저장 — CAD 대응 실물이 없을 때 합성 점군 배포용.</summary>
    [RelayCommand]
    private void SaveCloud()
    {
        if (_scanCloud == null)
        {
            _dialogService.ShowMessage("저장할 점군이 없습니다. 먼저 점군을 열거나 합성 점군을 생성하세요.", "점군 저장");
            return;
        }
        var path = _dialogService.ShowSaveCloudDialog("synthetic_scan.vpc");
        if (path == null) return;
        PointCloudService.SaveVpc(path, _scanCloud);
        StatusText = $"점군 저장 완료: {_scanCloud.Count:N0}점 → {path}";
    }

    /// <summary>진단 모드용 — 점군을 지정 경로에 저장 / 파일에서 로드.</summary>
    public void SaveCloudForDiagnostics(string path)
    {
        if (_scanCloud != null) PointCloudService.SaveVpc(path, _scanCloud);
    }

    public void LoadCloudForDiagnostics(string path)
    {
        var cloud = _cloudService.LoadCloud(path);
        SetScanCloud(cloud, $"점군 로드: {Path.GetFileName(path)} — {cloud.Count:N0}점 (미정합)");
    }

    private void SetScanCloud(List<Point3D> cloud, string status)
    {
        _scanCloud = cloud;
        _tAlign = null;
        RegistrationStatus = "미정합";
        // 그라인딩 전처리·영역은 점군에 종속 — 새 점군이면 무효화
        _prepCloud = null;
        PrepStatus = "전처리 대기";
        Regions.Clear();
        SelectedRegion = null;
        StatusText = status;
        HighlightChanged?.Invoke(this, EventArgs.Empty);
        if (IsGrindingMode) _ = PreprocessIfNeededAsync();
    }

    // ---- 그라인딩 모드 (스캔 명세 Step S1·S2: 전처리 + 영역 선택) ----

    /// <summary>공정 모드 — false=용접(CAD+엣지), true=그라인딩(점군+영역).</summary>
    [ObservableProperty]
    private bool _isGrindingMode;

    /// <summary>선택 도구 — false=라쏘(드래그로 다각형), true=브러시(드래그 궤적 원).</summary>
    [ObservableProperty]
    private bool _isBrushMode;

    [ObservableProperty]
    private double _brushRadiusPx = 25;

    /// <summary>라쏘/브러시 깊이 밴드(mm) — 후보의 전방 깊이 + 이 값 이내만 선택 (0 = 필터 없음).</summary>
    [ObservableProperty]
    private double _selectionDepthBandMm = 20;

    [ObservableProperty]
    private string _prepStatus = "점군 없음";

    [ObservableProperty]
    private SampleSurfaceKind _selectedSampleKind = SampleSurfaceKind.SineBump;

    public SampleSurfaceKind[] SampleKinds { get; } = Enum.GetValues<SampleSurfaceKind>();

    /// <summary>선택된 가공 영역들 — 목록 순서가 곧 가공 순서다 (용접 경로 목록과 동일 규약).</summary>
    public ObservableCollection<GrindingRegion> Regions { get; } = new();

    [ObservableProperty]
    private GrindingRegion? _selectedRegion;

    private PreprocessedCloud? _prepCloud;
    private int _regionSeq;

    /// <summary>그라인딩 모드 표시·선택 대상 점군 (전처리 결과).</summary>
    public IReadOnlyList<Point3D> GrindingPoints =>
        _prepCloud?.Points ?? (IReadOnlyList<Point3D>)Array.Empty<Point3D>();

    partial void OnIsGrindingModeChanged(bool value)
    {
        HoveredEdgeId = 0;
        StatusText = value
            ? "그라인딩 모드 — 점군을 열거나 합성 표면을 생성한 뒤, 뷰포트를 드래그해 영역을 선택하세요."
            : "용접 모드 — STEP 파일을 열고 용접 모서리를 클릭하세요.";
        if (value) _ = PreprocessIfNeededAsync();
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedRegionChanged(GrindingRegion? value)
        => HighlightChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>합성 표면 점군 생성 — CAD·실물 없이 그라인딩 흐름을 시험 (σ=0.05mm 노이즈).</summary>
    [RelayCommand]
    private void GenerateGrindingSample()
    {
        var sc = SampleCloudGenerator.Generate(SelectedSampleKind, 0.05);
        SetScanCloud(sc.Points, $"합성 표면 생성 — {sc.Name}, {sc.Points.Count:N0}점");
    }

    private async Task PreprocessIfNeededAsync()
    {
        if (_scanCloud == null || _prepCloud != null) return;
        IsBusy = true;
        PrepStatus = "전처리 중...";
        try
        {
            var cloud = _scanCloud;
            var prep = await Task.Run(() =>
            {
                // 법선 일관화 시점: 점군 상방 (VMS 구조광 상면 스캔 전제 — 스캔 좌표계 +Z 가 카메라 쪽)
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue,
                       maxY = double.MinValue, maxZ = double.MinValue;
                foreach (var p in cloud)
                {
                    minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
                    maxZ = Math.Max(maxZ, p.Z);
                }
                double extent = Math.Max(maxX - minX, maxY - minY);
                var viewpoint = new Point3D((minX + maxX) / 2, (minY + maxY) / 2,
                    maxZ + Math.Max(extent, 100) * 5);
                return _prepService.Process(cloud, viewpoint);
            });
            _prepCloud = prep;
            PrepStatus = prep.Summary;
            StatusText = $"전처리 완료 — {prep.Summary}. 드래그로 영역을 선택하세요.";
        }
        catch (Exception ex)
        {
            PrepStatus = "전처리 실패";
            StatusText = $"전처리 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            HighlightChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>선택 결과를 영역에 반영하는 방식 — 새 영역 / 선택 영역에 추가 / 모든 영역에서 제거.</summary>
    public enum SelectionCombine { NewRegion, Add, Remove }

    /// <summary>라쏘 완료 — 뷰가 화면 다각형과 투영 델리게이트를 넘긴다.</summary>
    public void ApplyLassoSelection(IReadOnlyList<System.Windows.Point> polygon,
        RegionSelectService.ProjectFunc project, SelectionCombine combine)
    {
        if (_prepCloud == null) { StatusText = "먼저 점군을 열거나 합성 표면을 생성하세요."; return; }
        double band = combine == SelectionCombine.Remove ? 0 : SelectionDepthBandMm;
        var idx = RegionSelectService.SelectByPolygon(_prepCloud.Points, project, polygon, band);
        ApplySelection(idx, combine);
    }

    /// <summary>브러시 스트로크 완료 — 드래그 궤적(중심점들) 반경 이내 선택.</summary>
    public void ApplyBrushSelection(IReadOnlyList<System.Windows.Point> strokeCenters,
        RegionSelectService.ProjectFunc project, SelectionCombine combine)
    {
        if (_prepCloud == null) { StatusText = "먼저 점군을 열거나 합성 표면을 생성하세요."; return; }
        double band = combine == SelectionCombine.Remove ? 0 : SelectionDepthBandMm;
        var idx = RegionSelectService.SelectByStroke(_prepCloud.Points, project, strokeCenters,
            Math.Max(1, BrushRadiusPx), band);
        // 브러시는 수정 도구 — 조합 미지정(NewRegion)이면 선택 영역에 추가로 동작
        if (combine == SelectionCombine.NewRegion && SelectedRegion != null)
            combine = SelectionCombine.Add;
        ApplySelection(idx, combine);
    }

    private void ApplySelection(List<int> indices, SelectionCombine combine)
    {
        switch (combine)
        {
            case SelectionCombine.NewRegion:
                if (indices.Count == 0) { StatusText = "선택된 점이 없습니다 — 점군 위를 드래그하세요."; return; }
                var region = new GrindingRegion { RegionId = $"Region{++_regionSeq}", PointIndices = indices };
                Regions.Add(region);
                SelectedRegion = region;
                StatusText = $"[{Regions.Count}번] {region.RegionId} 추가: 점 {indices.Count:N0}개 — " +
                             $"총 {Regions.Count}개 영역 (Shift 드래그=추가, Ctrl 드래그=제거)";
                break;

            case SelectionCombine.Add:
                if (indices.Count == 0) { StatusText = "선택된 점이 없습니다."; return; }
                if (SelectedRegion == null) { ApplySelection(indices, SelectionCombine.NewRegion); return; }
                var set = new HashSet<int>(SelectedRegion.PointIndices);
                set.UnionWith(indices);
                SelectedRegion.PointIndices = set.ToList();
                StatusText = $"{SelectedRegion.RegionId} 에 추가 → 점 {SelectedRegion.PointIndices.Count:N0}개";
                break;

            case SelectionCombine.Remove:
                if (indices.Count == 0) { StatusText = "제거할 점이 없습니다."; return; }
                var remove = new HashSet<int>(indices);
                int removedTotal = 0;
                foreach (var r in Regions.ToList())
                {
                    int before = r.PointIndices.Count;
                    r.PointIndices = r.PointIndices.Where(i => !remove.Contains(i)).ToList();
                    removedTotal += before - r.PointIndices.Count;
                    if (r.PointIndices.Count == 0)
                    {
                        Regions.Remove(r);
                        if (SelectedRegion == r) SelectedRegion = Regions.LastOrDefault();
                    }
                }
                StatusText = $"영역에서 점 {removedTotal:N0}개 제거 — 남은 영역 {Regions.Count}개";
                break;
        }
        System.Windows.Data.CollectionViewSource.GetDefaultView(Regions)?.Refresh();
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveRegion(GrindingRegion? region)
    {
        if (region == null) return;
        Regions.Remove(region);
        if (SelectedRegion == region) SelectedRegion = Regions.LastOrDefault();
        StatusText = $"{region.RegionId} 제거 — 남은 영역 {Regions.Count}개";
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearRegions()
    {
        Regions.Clear();
        SelectedRegion = null;
        StatusText = "모든 영역을 지웠습니다.";
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RunIcpAsync()
    {
        if (Model == null || _scanCloud == null)
        {
            _dialogService.ShowMessage("STEP 모델과 점군이 모두 필요합니다.", "정합");
            return;
        }
        IsBusy = true;
        StatusText = "ICP 정합 중...";
        try
        {
            var model = Model;
            var result = await Task.Run(() =>
            {
                _cadSamples ??= PointCloudService.SampleModelSurfaceDetailed(model, 15000);
                var s = _cadSamples.Value;
                return _icpService.Register(_scanCloud, s.Points,
                    sampleTriIndex: s.TriIndex, triangles: s.Triangles);
            });
            _tAlign = result.CadToScan;
            RegistrationStatus = $"RMSE {result.RmseMm:F3} mm · 인라이어 {result.InlierRatio:P0} · {result.Iterations}회";
            StatusText = $"정합 완료 — RMSE {result.RmseMm:F3} mm, 인라이어 {result.InlierRatio:P0}, " +
                         $"{result.Iterations}회 반복{(result.Converged ? "" : " (미수렴)")} — 내보내기는 로봇 좌표계로 변환됩니다";
            HighlightChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = $"정합 실패: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private bool _syncingSpacing;   // 선택 변경으로 값을 동기화할 때 재계산 루프 방지

    partial void OnSelectedPathSpacingMmChanged(double value)
    {
        if (_syncingSpacing || Model == null || SelectedContour == null) return;
        SelectedContour.SpacingMm = Math.Clamp(value, 0.1, 100.0);
        RecomputeContour(SelectedContour);
    }

    partial void OnSelectedPathAdaptiveChanged(bool value)
    {
        if (_syncingSpacing || Model == null || SelectedContour == null) return;
        SelectedContour.Adaptive = value;
        RecomputeContour(SelectedContour);
    }

    private void RecomputeContour(WeldingPathContour c)
    {
        c.Poses = _poseService.ComputePoses(c, Model!, c.SpacingMm, c.Adaptive);
        RefreshAfterPoseChange($"{c.PathId} 간격 {c.SpacingMm:0.#} mm{(c.Adaptive ? " (곡률 적응)" : "")} " +
                               $"→ 포즈 {c.Poses.Count}개");
    }

    /// <summary>툴바 기본 간격·적응 설정을 모든 경로에 일괄 적용.</summary>
    [RelayCommand]
    private void ApplySpacingToAll()
    {
        if (Model == null || Contours.Count == 0) return;
        foreach (var c in Contours)
        {
            c.SpacingMm = Math.Clamp(PoseSpacingMm, 0.1, 100.0);
            c.Adaptive = AdaptiveSampling;
            c.Poses = _poseService.ComputePoses(c, Model, c.SpacingMm, c.Adaptive);
        }
        SyncSelectedSpacing();
        RefreshAfterPoseChange($"간격 {PoseSpacingMm:0.#} mm{(AdaptiveSampling ? " (곡률 적응)" : "")} 전체 적용 — " +
                               $"경로 {Contours.Count}개, 총 포즈 {Contours.Sum(c => c.Poses.Count)}개");
    }

    private void SyncSelectedSpacing()
    {
        _syncingSpacing = true;
        SelectedPathSpacingMm = SelectedContour?.SpacingMm ?? PoseSpacingMm;
        SelectedPathAdaptive = SelectedContour?.Adaptive ?? AdaptiveSampling;
        _syncingSpacing = false;
    }

    private void RefreshAfterPoseChange(string status)
    {
        System.Windows.Data.CollectionViewSource.GetDefaultView(Contours)?.Refresh();
        var sel = SelectedContour;
        Poses.Clear();
        if (sel != null)
            foreach (var p in sel.Poses) Poses.Add(p);
        StatusText = status;
        HighlightChanged?.Invoke(this, EventArgs.Empty);
    }

    public ObservableCollection<TorchPose> Poses { get; } = new();

    partial void OnSelectedContourChanged(WeldingPathContour? value)
    {
        Poses.Clear();
        if (value != null)
            foreach (var p in value.Poses) Poses.Add(p);
        SyncSelectedSpacing();
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
            _cadSamples = null;      // 새 모델 — CAD 샘플·기존 정합 무효화
            _tAlign = null;
            RegistrationStatus = _scanCloud == null ? "점군 없음" : "미정합";
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

        contour.SpacingMm = Math.Clamp(PoseSpacingMm, 0.1, 100.0);
        contour.Adaptive = AdaptiveSampling;
        contour.Poses = _poseService.ComputePoses(contour, Model, contour.SpacingMm, contour.Adaptive);
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
        _poseService.ExportJson(path, Contours.ToList(), _tAlign);
        StatusText = _tAlign.HasValue
            ? $"내보내기 완료 (로봇 좌표계, T_align 적용): 경로 {Contours.Count}개 → {path}"
            : $"내보내기 완료 (CAD 좌표계 — 정합 전): 경로 {Contours.Count}개 → {path}";
    }
}
