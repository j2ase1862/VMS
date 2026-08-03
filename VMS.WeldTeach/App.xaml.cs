using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;
using VMS.WeldTeach.Services;
using VMS.WeldTeach.ViewModels;
using VMS.WeldTeach.Views;

namespace VMS.WeldTeach;

public partial class App : Application
{
    static App()
    {
        // double 바인딩 + UpdateSourceTrigger=PropertyChanged 인 TextBox 에서 "1." 같은
        // 중간 입력이 즉시 재파싱-역기입되며 소수점이 지워지는 WPF 기본 동작을 끈다
        // (포즈 간격/피킹 임계값 등 소수 입력 허용). 모든 TextBox 생성 전에 설정해야 한다.
        System.Windows.FrameworkCompatibilityPreferences
            .KeepTextBoxDisplaySynchronizedWithTextProperty = false;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Occt.NET 벤더 홍보 팝업 감시·클로킹 (STEP 리더/라이터 사용 시 출현)
        VendorPopupSuppressor.Install();

        // 커스텀 크롬(WindowStyle=None) 타이틀바의 min/max/close 버튼이 쓰는
        // SystemCommands 를 모든 Window 에 클래스 수준으로 연결 (VMS 본체와 동일 방식)
        RegisterWindowChromeCommandBindings();

        // 서비스 수동 구성 (VMS 관례 — DI 컨테이너 없이 App 에서 조립)
        var cadKernel = new CadKernelService();
        var dialogService = new DialogService();
        var chainService = new EdgeChainService();
        var poseService = new TorchPoseService();
        var cloudService = new PointCloudService();
        var icpService = new IcpService();
        var viewModel = new MainViewModel(cadKernel, dialogService, chainService, poseService,
            cloudService, icpService, new CloudPreprocessService(), new CoveragePathService());

        // 헤드리스 자가 검증 모드: 커널 체인을 실행해 로그 파일에 결과를 남기고 종료
        if (e.Args.Contains("--selftest"))
        {
            RunSelfTest(cadKernel, chainService, poseService);
            Shutdown(0);
            return;
        }

        // 헤드리스 분석 모드: --analyze <step> [엣지Id] — 엣지 연결성/체이닝 진단 덤프
        int anIdx = Array.IndexOf(e.Args, "--analyze");
        if (anIdx >= 0 && anIdx + 1 < e.Args.Length)
        {
            int? seedId = anIdx + 2 < e.Args.Length && int.TryParse(e.Args[anIdx + 2], out var sid) ? sid : null;
            RunAnalyze(cadKernel, chainService, e.Args[anIdx + 1], seedId);
            Shutdown(0);
            return;
        }

        var window = new MainWindow(viewModel);
        window.Show();

        // 진단 모드: --grindcap <png경로> — 그라인딩 모드 전환 → 합성 표면 생성·전처리 →
        // 뷰포트 중앙 라쏘 선택 → 캡처. GUI 와 동일한 선택 경로(투영 델리게이트)를 검증한다.
        int grindIdx = Array.IndexOf(e.Args, "--grindcap");
        if (grindIdx >= 0 && grindIdx + 1 < e.Args.Length)
        {
            string grindPng = e.Args[grindIdx + 1];
            _ = window.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    viewModel.IsGrindingMode = true;
                    viewModel.GenerateGrindingSampleCommand.Execute(null);
                    // 전처리(백그라운드) 완료 대기
                    for (int i = 0; i < 100 && viewModel.IsBusy; i++) await Task.Delay(100);
                    window.ZoomExtentsForDiagnostics();
                    await Task.Delay(800);   // 렌더 안정화
                    window.DiagSelectCenterRegion();
                    await Task.Delay(400);
                    await viewModel.GenerateCoverageCommand.ExecuteAsync(null);
                    await Task.Delay(800);
                    window.CaptureToPng(grindPng);
                    if (!e.Args.Contains("--stay")) Shutdown(0);
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "weldteach_diag.log"),
                            $"[{DateTime.Now:HH:mm:ss.fff}] grindcap error: {ex}{Environment.NewLine}");
                    }
                    catch { }
                }
            });
            return;
        }

        // 진단 모드: --weldcap <png경로> — 용접 모드 샘플 시편 생성 → 캡처.
        // 용접 툴바가 가장 긴 행이라 폰트/여백 변경 시 넘침 확인용으로도 쓴다.
        int weldIdx = Array.IndexOf(e.Args, "--weldcap");
        if (weldIdx >= 0 && weldIdx + 1 < e.Args.Length)
        {
            string weldPng = e.Args[weldIdx + 1];
            _ = window.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await viewModel.GenerateSampleCommand.ExecuteAsync(null);
                    for (int i = 0; i < 100 && viewModel.IsBusy; i++) await Task.Delay(100);
                    window.ZoomExtentsForDiagnostics();
                    await Task.Delay(800);   // 렌더 안정화
                    window.CaptureToPng(weldPng);
                    if (!e.Args.Contains("--stay")) Shutdown(0);
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "weldteach_diag.log"),
                            $"[{DateTime.Now:HH:mm:ss.fff}] weldcap error: {ex}{Environment.NewLine}");
                    }
                    catch { }
                }
            });
            return;
        }

        // 진단 모드: --open <step경로> [--capture <png경로>]
        // GUI 와 동일한 로드 경로(LoadAsync)를 타고, 캡처 지정 시 렌더 결과를 저장 후 종료한다.
        int openIdx = Array.IndexOf(e.Args, "--open");
        if (openIdx >= 0 && openIdx + 1 < e.Args.Length)
        {
            string stepPath = e.Args[openIdx + 1];
            int capIdx = Array.IndexOf(e.Args, "--capture");
            string? capturePath = capIdx >= 0 && capIdx + 1 < e.Args.Length ? e.Args[capIdx + 1] : null;
            var trace = Path.Combine(Path.GetTempPath(), "weldteach_diag.log");
            void T(string m) { try { File.AppendAllText(trace, $"[{DateTime.Now:HH:mm:ss.fff}] {m}{Environment.NewLine}"); } catch { } }
            _ = window.Dispatcher.InvokeAsync(async () =>
            {
                T("diag start");
                try
                {
                    await viewModel.LoadForDiagnosticsAsync(stepPath);
                    T($"load returned, status={viewModel.StatusText}");
                    int spIdx = Array.IndexOf(e.Args, "--spacing");
                    if (spIdx >= 0 && spIdx + 1 < e.Args.Length &&
                        double.TryParse(e.Args[spIdx + 1], out double spacing))
                    {
                        viewModel.PoseSpacingMm = spacing;
                        T($"spacing={spacing}");
                    }
                    if (e.Args.Contains("--adaptive")) { viewModel.AdaptiveSampling = true; T("adaptive=on"); }
                    if (e.Args.Contains("--showall")) { viewModel.ShowAllPaths = true; T("showall=on"); }
                    if (e.Args.Contains("--synthcloud"))
                    {
                        viewModel.GenerateSampleCloudCommand.Execute(null);
                        T($"synthcloud, status={viewModel.StatusText}");
                    }
                    int cloudIdx = Array.IndexOf(e.Args, "--cloud");
                    if (cloudIdx >= 0 && cloudIdx + 1 < e.Args.Length)
                    {
                        viewModel.LoadCloudForDiagnostics(e.Args[cloudIdx + 1]);
                        T($"cloud loaded, status={viewModel.StatusText}");
                    }
                    int saveIdx = Array.IndexOf(e.Args, "--savecloud");
                    if (saveIdx >= 0 && saveIdx + 1 < e.Args.Length)
                    {
                        viewModel.SaveCloudForDiagnostics(e.Args[saveIdx + 1]);
                        T($"cloud saved: {e.Args[saveIdx + 1]}");
                    }
                    if (e.Args.Contains("--icp"))
                    {
                        await viewModel.RunIcpCommand.ExecuteAsync(null);
                        T($"icp done, status={viewModel.StatusText}");
                    }
                    int pickIdx = Array.IndexOf(e.Args, "--pick");
                    if (pickIdx >= 0 && pickIdx + 1 < e.Args.Length)
                    {
                        // 쉼표 구분 다중 지정 가능: --pick 227,229
                        foreach (var tok in e.Args[pickIdx + 1].Split(','))
                        {
                            if (!int.TryParse(tok, out int pickEdgeId)) continue;
                            viewModel.SelectEdge(pickEdgeId);
                            T($"picked #{pickEdgeId}, status={viewModel.StatusText}");
                        }
                    }
                    if (capturePath != null)
                    {
                        await Task.Delay(1500);   // 렌더 안정화 대기
                        T("capturing");
                        window.CaptureToPng(capturePath);
                        T("captured");
                        if (!e.Args.Contains("--stay")) Shutdown(0);
                    }
                }
                catch (Exception ex) { T("diag error: " + ex); }
            });
        }
    }

    private static void RegisterWindowChromeCommandBindings()
    {
        System.Windows.Input.CommandManager.RegisterClassCommandBinding(typeof(Window),
            new System.Windows.Input.CommandBinding(SystemCommands.MinimizeWindowCommand,
                (s, e) => { if (s is Window w) SystemCommands.MinimizeWindow(w); }));
        System.Windows.Input.CommandManager.RegisterClassCommandBinding(typeof(Window),
            new System.Windows.Input.CommandBinding(SystemCommands.MaximizeWindowCommand,
                (s, e) =>
                {
                    if (s is Window w)
                    {
                        if (w.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(w);
                        else SystemCommands.MaximizeWindow(w);
                    }
                }));
        System.Windows.Input.CommandManager.RegisterClassCommandBinding(typeof(Window),
            new System.Windows.Input.CommandBinding(SystemCommands.RestoreWindowCommand,
                (s, e) => { if (s is Window w) SystemCommands.RestoreWindow(w); }));
        System.Windows.Input.CommandManager.RegisterClassCommandBinding(typeof(Window),
            new System.Windows.Input.CommandBinding(SystemCommands.CloseWindowCommand,
                (s, e) => { if (s is Window w) SystemCommands.CloseWindow(w); }));
    }

    private static void RunAnalyze(CadKernelService kernel, EdgeChainService chain, string stepPath, int? seedId)
    {
        var log = Path.Combine(Path.GetTempPath(), "weldteach_analyze.log");
        try
        {
            var lines = new List<string>();
            var model = kernel.LoadStep(stepPath);
            lines.Add($"file: {stepPath}");
            lines.Add($"model: solids={model.SolidCount} faces={model.FaceCount} edges={model.Edges.Count}");

            // 엣지 간 최소 끝점 간격 분포 — 체이닝 JoinTolerance 적합성 진단
            var gaps = new List<double>();
            foreach (var a in model.Edges)
            {
                if (a.IsClosed) continue;
                double best = double.MaxValue;
                foreach (var b in model.Edges)
                {
                    if (b.EdgeId == a.EdgeId || b.IsClosed) continue;
                    foreach (var pa in new[] { a.StartPoint, a.EndPoint })
                        foreach (var pb in new[] { b.StartPoint, b.EndPoint })
                            best = Math.Min(best, (pa - pb).Length);
                }
                if (best < double.MaxValue) gaps.Add(best);
            }
            gaps.Sort();
            string Pct(double q) => gaps.Count == 0 ? "-" : gaps[(int)Math.Min(gaps.Count - 1, q * gaps.Count)].ToString("E2");
            lines.Add($"endpoint gaps: n={gaps.Count} p50={Pct(0.5)} p90={Pct(0.9)} max={(gaps.Count > 0 ? gaps[^1].ToString("E2") : "-")}");
            lines.Add($"closed edges: {model.Edges.Count(x => x.IsClosed)}");

            // 체이닝 시험 — seedId 미지정 시 가장 긴 열린 엣지부터 상위 5개
            var seeds = seedId.HasValue
                ? model.Edges.Where(x => x.EdgeId == seedId.Value).ToList()
                : model.Edges.OrderByDescending(x => x.Length).Take(5).ToList();
            foreach (var s in seeds)
            {
                var c = chain.BuildChain(model, s.EdgeId);
                lines.Add($"chain from #{s.EdgeId} (len={s.Length:F1} closed={s.IsClosed} adjFaces={s.AdjacentFaceIds.Count}): " +
                          $"edges={c.EdgeIds.Count} [{string.Join(",", c.EdgeIds)}] totalLen={c.TotalLength:F1}");
            }
            lines.Add("ANALYZE OK");
            File.WriteAllLines(log, lines);
        }
        catch (Exception ex)
        {
            File.WriteAllText(log, "ANALYZE FAIL: " + ex);
        }
    }

    private static void RunSelfTest(CadKernelService kernel, EdgeChainService chain, TorchPoseService pose)
    {
        var log = Path.Combine(Path.GetTempPath(), "weldteach_selftest.log");
        try
        {
            var lines = new List<string>();
            var stepPath = Path.Combine(Path.GetTempPath(), "weldteach_selftest.step");
            kernel.GenerateSampleStep(stepPath);
            lines.Add($"sample: {stepPath} ({new FileInfo(stepPath).Length} bytes)");

            var model = kernel.LoadStep(stepPath);
            lines.Add($"model: solids={model.SolidCount} faces={model.FaceCount} edges={model.Edges.Count} meshes={model.FaceMeshes.Count}");
            lines.Add($"mesh tris total={model.FaceMeshes.Sum(m => m.TriangleIndices.Count) / 3}");

            // 용접 심: 리브(y∈[26,34]) 하단이 베이스 상면(z=8)과 만나는 T-필릿 라인
            var seam = model.Edges.FirstOrDefault(ed =>
                Math.Abs(ed.StartPoint.Z - 8) < 1e-6 && Math.Abs(ed.EndPoint.Z - 8) < 1e-6
                && Math.Abs(ed.StartPoint.Y - 26) < 1e-6 && Math.Abs(ed.EndPoint.Y - 26) < 1e-6
                && ed.AdjacentFaceIds.Count == 2 && ed.Length > 30);
            if (seam == null) { lines.Add("FAIL: seam edge not found"); }
            else
            {
                var contour = chain.BuildChain(model, seam.EdgeId);
                lines.Add($"chain: edges={contour.EdgeIds.Count} len={contour.TotalLength:F1} pts={contour.PathPoints.Count}");
                var poses = pose.ComputePoses(contour, model);
                var bis = pose.ComputeBisector(contour, model);
                lines.Add($"bisector: ({bis.X:F3},{bis.Y:F3},{bis.Z:F3})");
                lines.Add($"poses: {poses.Count} first=({poses[0].X:F1},{poses[0].Y:F1},{poses[0].Z:F1} R{poses[0].RollDeg:F1} P{poses[0].PitchDeg:F1} Y{poses[0].YawDeg:F1})");

                // 파이널라이저 안정성 검증 — 래퍼 결함 회피(OcctLifetime)가 유효한지 강제 GC 로 확인
                for (int round = 0; round < 3; round++)
                {
                    _ = kernel.LoadStep(stepPath);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                lines.Add("gc hammer: OK (3 rounds load+collect)");

                // ICP 정합 검증 — 기지 오프셋 합성 스캔(상면 부분 가시 + 0.05mm 노이즈)을
                // CAD 샘플에 정합해 T_align 을 복원, 명세서 기준(RMSE ≤ 0.2mm) 확인
                var (synthCloud, tTrue) = PointCloudService.GenerateSyntheticScan(model, 0.05, keepAboveZ: 6.0);
                var samples = PointCloudService.SampleModelSurfaceDetailed(model, 15000);
                var icp = new IcpService().Register(synthCloud, samples.Points,
                    sampleTriIndex: samples.TriIndex, triangles: samples.Triangles);
                lines.Add($"icp: rmse={icp.RmseMm:F4}mm inlier={icp.InlierRatio:P0} iters={icp.Iterations} converged={icp.Converged}");

                // T_align ↔ 기지 변환 비교 — 시편 영역 점들을 양쪽으로 변환한 최대 편차
                double maxDev = 0;
                foreach (var tp in new[] { new Point3D(0,0,0), new Point3D(100,60,8), new Point3D(50,30,38), new Point3D(20,26,8) })
                    maxDev = Math.Max(maxDev, (icp.CadToScan.Transform(tp) - tTrue.Transform(tp)).Length);
                lines.Add($"icp: T_align max deviation vs ground truth = {maxDev:F4} mm");

                // 포즈 변환 검증 — 심 시작 포즈를 로봇 좌표로 옮겨 기지 변환 결과와 비교
                var pose0 = poses[0];
                var moved = TorchPoseService.TransformPose(pose0, icp.CadToScan);
                var expect = tTrue.Transform(new Point3D(pose0.X, pose0.Y, pose0.Z));
                lines.Add($"icp: seam pose→robot ({moved.X:F2},{moved.Y:F2},{moved.Z:F2}) expect ({expect.X:F2},{expect.Y:F2},{expect.Z:F2})");

                lines.Add(icp.RmseMm <= 0.2 && maxDev <= 0.3 ? "ICP OK" : "ICP FAIL");

                // VPC 라운드트립 — VMS [Save 3D] 포맷과의 호환 검증
                var vpcPath = Path.Combine(Path.GetTempPath(), "weldteach_selftest.vpc");
                PointCloudService.SaveVpc(vpcPath, synthCloud);
                var vpcLoaded = new PointCloudService().LoadCloud(vpcPath);
                double vpcMax = 0;
                for (int i = 0; i < Math.Min(100, vpcLoaded.Count); i++)
                    vpcMax = Math.Max(vpcMax, (vpcLoaded[i] - synthCloud[i]).Length);
                lines.Add(vpcLoaded.Count == synthCloud.Count && vpcMax < 1e-3
                    ? $"vpc roundtrip: OK ({vpcLoaded.Count:N0}점, 편차 {vpcMax:E1})"
                    : $"vpc roundtrip: FAIL ({vpcLoaded.Count}/{synthCloud.Count}, {vpcMax})");

                lines.Add("SELFTEST OK");
            }

            // 그라인딩 확장 M1 — 합성 점군 → 전처리·법선 추정 검증 (OCCT 불필요)
            RunGrindingM1SelfTest(lines);

            // 그라인딩 확장 M2 — 라쏘/브러시 영역 선택 검증
            RunGrindingM2SelfTest(lines);

            // 그라인딩 확장 M3 — 커버리지 스캔라인 생성 검증 (평면·경사면)
            RunGrindingM3SelfTest(lines);

            // 그라인딩 확장 M4 — 곡면 추종·구멍 분할·스텝오버 3D 보정 검증
            RunGrindingM4SelfTest(lines);

            File.WriteAllLines(log, lines);
        }
        catch (Exception ex)
        {
            File.WriteAllText(log, "SELFTEST FAIL: " + ex);
        }
    }

    /// <summary>
    /// 그라인딩 스캔 명세 §6.1 — 5종 합성 표면 × 노이즈(σ=0 / 0.15mm)에서 전처리 후
    /// 추정 법선 vs 해석 법선 각도 오차(평균 ≤2° / ≤5°)와 이상치 제거를 검증한다.
    /// </summary>
    private static void RunGrindingM1SelfTest(List<string> lines)
    {
        var prep = new CloudPreprocessService();
        bool allOk = true;

        foreach (var kind in Enum.GetValues<SampleSurfaceKind>())
        {
            foreach (double sigma in new[] { 0.0, 0.15 })
            {
                var sc = SampleCloudGenerator.Generate(kind, sigma);
                var pc = prep.Process(sc.Points, sc.Viewpoint);

                var errors = new double[pc.Points.Count];
                for (int i = 0; i < pc.Points.Count; i++)
                {
                    double dot = Math.Clamp(Vector3D.DotProduct(
                        pc.Normals[i], sc.TrueNormalAt(pc.Points[i])), -1.0, 1.0);
                    errors[i] = Math.Acos(dot) * 180.0 / Math.PI;
                }
                double mean = errors.Average();
                Array.Sort(errors);
                double p95 = errors[(int)(errors.Length * 0.95)];

                double limit = sigma == 0 ? 2.0 : 5.0;
                bool ok = mean <= limit;
                allOk &= ok;
                lines.Add($"m1 {sc.Name} σ={sigma:0.00}: {pc.Summary} · " +
                          $"법선오차 mean={mean:F2}° p95={p95:F2}° (한계 {limit}°) {(ok ? "OK" : "FAIL")}");
            }
        }

        // 이상치 제거 — 평면 + 부유점 200개 주입 → 표면(z≈0)에서 1mm 이상 뜬 점이 남지 않아야 함
        {
            var sc = SampleCloudGenerator.Generate(SampleSurfaceKind.Plane, 0.05, outlierCount: 200);
            var pc = prep.Process(sc.Points, sc.Viewpoint);
            int floating = pc.Points.Count(p => Math.Abs(p.Z) > 1.0);
            bool ok = floating == 0 && pc.OutlierRemovedCount >= 150;
            allOk &= ok;
            lines.Add($"m1 outlier: 주입 200 → 제거 {pc.OutlierRemovedCount}, 잔존 부유점 {floating} {(ok ? "OK" : "FAIL")}");
        }

        lines.Add(allOk ? "GRINDING M1 OK" : "GRINDING M1 FAIL");
    }

    /// <summary>
    /// 그라인딩 스캔 명세 §6.2 — 라쏘 다각형/브러시 선택 정확성, 첫 레이어 깊이 밴드,
    /// 10만 점 선택 응답 시간을 정사영 프로젝터(화면 1px = 1mm, 상방 카메라)로 검증한다.
    /// </summary>
    private static void RunGrindingM2SelfTest(List<string> lines)
    {
        bool allOk = true;
        // 정사영 프로젝터 — 화면 (x,y) = 월드 (X,Y), 깊이 = 500 − Z (카메라가 +Z 상방)
        RegionSelectService.ProjectFunc proj =
            p => new ProjectedPoint(p.X, p.Y, 500 - p.Z, true);

        var sc = SampleCloudGenerator.Generate(SampleSurfaceKind.Plane);   // σ=0 평면
        var rect = new[]
        {
            new Point(20, 15), new Point(80, 15), new Point(80, 45), new Point(20, 45),
        };

        // 1) 라쏘 사각형 — 내부 점 전부·외부 점 0 (좌표 판정과 다각형 판정의 일치)
        {
            var idx = RegionSelectService.SelectByPolygon(sc.Points, proj, rect, 20);
            int expected = sc.Points.Count(p => p.X > 20 && p.X < 80 && p.Y > 15 && p.Y < 45);
            bool inside = idx.All(i =>
                sc.Points[i].X >= 20 && sc.Points[i].X <= 80 &&
                sc.Points[i].Y >= 15 && sc.Points[i].Y <= 45);
            bool ok = idx.Count == expected && idx.Count > 0 && inside;
            allOk &= ok;
            lines.Add($"m2 라쏘: 선택 {idx.Count:N0} / 기대 {expected:N0}, 내부만={inside} {(ok ? "OK" : "FAIL")}");
        }

        // 2) 깊이 밴드 — 상면(z=0) + 30mm 아래 복제 레이어 → 첫 레이어만 선택돼야 함
        {
            var doubled = sc.Points
                .Concat(sc.Points.Select(p => new Point3D(p.X, p.Y, p.Z - 30)))
                .ToList();
            var idx = RegionSelectService.SelectByPolygon(doubled, proj, rect, 20);
            int backLayer = idx.Count(i => doubled[i].Z < -1);
            var idxNoBand = RegionSelectService.SelectByPolygon(doubled, proj, rect, 0);
            bool ok = backLayer == 0 && idx.Count > 0 && idxNoBand.Count > idx.Count;
            allOk &= ok;
            lines.Add($"m2 깊이밴드: 상층 {idx.Count:N0} 선택, 하층 혼입 {backLayer} " +
                      $"(밴드 없음 {idxNoBand.Count:N0}) {(ok ? "OK" : "FAIL")}");
        }

        // 3) 브러시 스트로크 — 궤적 반경 이내 점만
        {
            var stroke = new[] { new Point(40, 30), new Point(50, 30), new Point(60, 30) };
            double r = 10;
            var idx = RegionSelectService.SelectByStroke(sc.Points, proj, stroke, r, 20);
            bool within = idx.All(i => stroke.Any(c =>
            {
                double dx = sc.Points[i].X - c.X, dy = sc.Points[i].Y - c.Y;
                return dx * dx + dy * dy <= r * r + 1e-9;
            }));
            int expected = sc.Points.Count(p => stroke.Any(c =>
            {
                double dx = p.X - c.X, dy = p.Y - c.Y;
                return dx * dx + dy * dy <= r * r;
            }));
            bool ok = within && idx.Count == expected && idx.Count > 0;
            allOk &= ok;
            lines.Add($"m2 브러시: 선택 {idx.Count:N0} / 기대 {expected:N0}, 반경내만={within} {(ok ? "OK" : "FAIL")}");
        }

        // 4) 성능 — 10만 점 라쏘 선택 (명세 목표 100ms, 한계 250ms)
        {
            var rng = new Random(42);
            var big = new List<Point3D>(100_000);
            for (int i = 0; i < 100_000; i++)
                big.Add(new Point3D(rng.NextDouble() * 100, rng.NextDouble() * 60, rng.NextDouble() * 5));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var idx = RegionSelectService.SelectByPolygon(big, proj, rect, 20);
            sw.Stop();
            bool ok = sw.ElapsedMilliseconds <= 250;
            allOk &= ok;
            lines.Add($"m2 성능: 100k점 라쏘 {sw.ElapsedMilliseconds}ms (선택 {idx.Count:N0}) {(ok ? "OK" : "FAIL")}");
        }

        lines.Add(allOk ? "GRINDING M2 OK" : "GRINDING M2 FAIL");
    }

    /// <summary>
    /// 그라인딩 스캔 명세 §6.3 — 평면·30° 경사면에서 커버리지 생성을 검증한다:
    /// 라인 수(반 스텝 인셋 배치), 인접 라인 3D 간격 ≤ 스텝오버×1.31, 지그재그 방향 교대,
    /// 커버리지(영역 점이 공구 반경 내) ≥ 99%, 2.5D 위반 0.
    /// </summary>
    private static void RunGrindingM3SelfTest(List<string> lines)
    {
        bool allOk = true;
        var prepSvc = new CloudPreprocessService();
        var covSvc = new CoveragePathService();
        var prm = new GrindingParams { ToolDiameterMm = 50, OverlapPct = 30 };   // 스텝오버 35

        foreach (var kind in new[] { SampleSurfaceKind.Plane, SampleSurfaceKind.InclinedPlane })
        {
            var sc = SampleCloudGenerator.Generate(kind, 0.05);
            var prep = prepSvc.Process(sc.Points, sc.Viewpoint);
            var all = Enumerable.Range(0, prep.Points.Count).ToList();
            var res = covSvc.Generate(prep.Points, prep.Normals, all, prm);

            // 라인 수 — 반 스텝 인셋 배치: 폭 60mm, 스텝오버 35mm → 17.5·42.5 두 줄
            double s = prm.StepoverMm;
            int expectLines = 60.0 <= s ? 1 : (int)Math.Ceiling((60.0 - s) / s - 1e-9) + 1;
            bool linesOk = res.LineCount == expectLines;

            // 인접 라인 3D 간격 — 각 라인 중앙점에서 이전 라인 폴리라인까지 최단 거리
            var byLine = res.Scanlines.GroupBy(s => s.LineIndex).OrderBy(g => g.Key)
                .Select(g => g.SelectMany(s => s.PathPoints).ToList()).ToList();
            double maxGap = 0;
            for (int li = 1; li < byLine.Count; li++)
            {
                var mid = byLine[li][byLine[li].Count / 2];
                double best = double.MaxValue;
                foreach (var p in byLine[li - 1]) best = Math.Min(best, (mid - p).Length);
                maxGap = Math.Max(maxGap, best);
            }
            bool gapOk = maxGap <= prm.StepoverMm * 1.31 + 0.5;

            // 지그재그 — 인접 라인 진행 방향이 반대
            bool zigOk = true;
            for (int li = 1; li < byLine.Count; li++)
            {
                var d0 = byLine[li - 1][^1] - byLine[li - 1][0];
                var d1 = byLine[li][^1] - byLine[li][0];
                if (Vector3D.DotProduct(d0, d1) >= 0) { zigOk = false; break; }
            }

            // 커버리지 — 영역 점이 스캔라인 점의 공구 반경(+여유) 이내
            var lineTree = new KdTree3(res.Scanlines.SelectMany(s => s.PathPoints).ToList());
            double radius = prm.ToolDiameterMm / 2 + 2;
            int covered = 0;
            foreach (var p in prep.Points)
            {
                lineTree.Nearest(p, out double d);
                if (d <= radius) covered++;
            }
            double coverage = (double)covered / prep.Points.Count;
            bool covOk = coverage >= 0.99;

            bool ok = linesOk && gapOk && zigOk && covOk && res.Ambiguous25DCells == 0;
            allOk &= ok;
            lines.Add($"m3 {sc.Name}: 라인 {res.LineCount}/{expectLines} · 최대간격 {maxGap:F1}mm " +
                      $"· 지그재그={zigOk} · 커버리지 {coverage:P1} · 길이 {res.TotalLengthMm:F0}mm " +
                      $"· 2.5D위반 {res.Ambiguous25DCells} {(ok ? "OK" : "FAIL")}");
        }

        lines.Add(allOk ? "GRINDING M3 OK" : "GRINDING M3 FAIL");
    }

    /// <summary>
    /// 그라인딩 스캔 명세 §6.3 (M4) — 곡면 추종 · 스텝오버 3D 보정 · 구멍 분할 · 노이즈 강건성.
    /// 원통 셸은 래스터를 축 방향(90°)으로 돌려 스텝오버가 곡률을 가로지르게 두고,
    /// 전개(unfold) 좌표에서 라인 간 표면 간격·커버리지를 잰다 — 주평면 등간격이면
    /// 가장자리 밴드에서 표면 간격이 스텝오버의 1.10배까지 벌어진다.
    /// </summary>
    private static void RunGrindingM4SelfTest(List<string> lines)
    {
        bool allOk = true;
        var prepSvc = new CloudPreprocessService();
        var covSvc = new CoveragePathService();
        const double R = SampleCloudGenerator.CylRadius;

        // 1) 원통 셸 — 스텝오버 3D 보정 + 곡면 추종 + 법선 정확도
        //    공구 30mm(스텝오버 21mm)로 잡아 라인이 급경사 밴드(φ≈30°)에 놓이게 한다.
        {
            var prm = new GrindingParams { ToolDiameterMm = 30, OverlapPct = 30, RasterAngleDeg = 90 };
            var sc = SampleCloudGenerator.Generate(SampleSurfaceKind.CylinderShell, 0.05);
            var prep = prepSvc.Process(sc.Points, sc.Viewpoint);
            var all = Enumerable.Range(0, prep.Points.Count).ToList();
            var res = covSvc.Generate(prep.Points, prep.Normals, all, prm);

            // 원통 전개 — (축 x, 호길이 R·φ). 전개면 유클리드 거리 = 표면 거리.
            static Point3D Unfold(Point3D p) => new(p.X, R * Math.Atan2(p.Y, p.Z + R), 0);

            double maxGap = MaxLineGapMm(res, Unfold);
            bool gapOk = maxGap <= prm.StepoverMm * 1.05;

            // 표면 추종 — 경로점의 해석 원통면 편차 ≤ 복셀 크기(1mm)
            double maxDev = 0;
            foreach (var s in res.Scanlines)
                foreach (var p in s.PathPoints)
                    maxDev = Math.Max(maxDev, sc.TrueDeviationAt(p));
            bool devOk = maxDev <= 1.0;

            // 법선 — 해석 법선 대비 평균 각도 오차
            double nErr = MeanNormalErrorDeg(res, sc);
            bool nOk = nErr <= 5.0;

            // 커버리지 — 전개면에서 공구 반경 이내 (여유 2mm = 경로 샘플 간격 보정)
            var tree = new KdTree3(res.Scanlines.SelectMany(s => s.PathPoints).Select(Unfold).ToList());
            double radius = prm.ToolDiameterMm / 2 + 2;
            int covered = 0;
            foreach (var p in prep.Points)
            {
                tree.Nearest(Unfold(p), out double d);
                if (d <= radius) covered++;
            }
            double coverage = (double)covered / prep.Points.Count;
            bool covOk = coverage >= 0.99;

            bool ok = gapOk && devOk && nOk && covOk;
            allOk &= ok;
            lines.Add($"m4 원통(전개): 라인 {res.LineCount} · 표면간격 {maxGap:F2}/{prm.StepoverMm:F1}mm " +
                      $"· 표면편차 {maxDev:F3}mm · 법선오차 {nErr:F2}° · 커버리지 {coverage:P1} " +
                      $"{(ok ? "OK" : "FAIL")}");
        }

        // 2) 사인 범프 — 래스터를 파형 가로지르게(90°) 돌린 곡면 스텝오버 보정
        {
            var prm = new GrindingParams { ToolDiameterMm = 30, OverlapPct = 30, RasterAngleDeg = 90 };
            var sc = SampleCloudGenerator.Generate(SampleSurfaceKind.SineBump, 0.05);
            var prep = prepSvc.Process(sc.Points, sc.Viewpoint);
            var all = Enumerable.Range(0, prep.Points.Count).ToList();
            var res = covSvc.Generate(prep.Points, prep.Normals, all, prm);

            // 파형 방향 전개 — u(x) = 표면 호길이(수치적분), v = y
            double k = 2 * Math.PI / SampleCloudGenerator.BumpWaveLen;
            const double du = 0.05;
            int steps = (int)(SampleCloudGenerator.PanelW / du);
            var arcTable = new double[steps + 1];
            for (int i = 0; i < steps; i++)
            {
                double slope = SampleCloudGenerator.BumpAmp * k * Math.Cos(k * (i + 0.5) * du);
                arcTable[i + 1] = arcTable[i] + du * Math.Sqrt(1 + slope * slope);
            }
            Point3D UnfoldBump(Point3D p)
            {
                double t = Math.Clamp(p.X / du, 0, steps - 1e-9);
                int i = (int)t;
                return new Point3D(arcTable[i] + (t - i) * (arcTable[i + 1] - arcTable[i]), p.Y, 0);
            }

            double maxGap = MaxLineGapMm(res, UnfoldBump);
            bool gapOk = maxGap <= prm.StepoverMm * 1.05;
            double maxDev = 0;
            foreach (var s in res.Scanlines)
                foreach (var p in s.PathPoints)
                    maxDev = Math.Max(maxDev, sc.TrueDeviationAt(p));
            double nErr = MeanNormalErrorDeg(res, sc);

            bool ok = gapOk && maxDev <= 1.0 && nErr <= 5.0 && res.Ambiguous25DCells == 0;
            allOk &= ok;
            lines.Add($"m4 사인범프(전개): 라인 {res.LineCount} · 표면간격 {maxGap:F2}/{prm.StepoverMm:F1}mm " +
                      $"· 표면편차 {maxDev:F3}mm · 법선오차 {nErr:F2}° " +
                      $"· 2.5D위반 {res.Ambiguous25DCells} {(ok ? "OK" : "FAIL")}");
        }

        // 3) 구멍 패널 — 세그먼트 분할 · 구멍 침범 없음 · marginMm 이격
        {
            var sc = SampleCloudGenerator.Generate(SampleSurfaceKind.HolePanel, 0.05);
            var prep = prepSvc.Process(sc.Points, sc.Viewpoint);
            var all = Enumerable.Range(0, prep.Points.Count).ToList();
            double cx = SampleCloudGenerator.PanelW / 2, cy = SampleCloudGenerator.PanelH / 2;

            static IEnumerable<Point3D> Pts(CoverageResult r) => r.Scanlines.SelectMany(s => s.PathPoints);
            double HoleDist(CoverageResult r) => Pts(r).Min(p =>
                Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)));

            var prm0 = new GrindingParams { ToolDiameterMm = 50, OverlapPct = 30 };
            var res0 = covSvc.Generate(prep.Points, prep.Normals, all, prm0);

            // 구멍을 가로지르는 라인은 2개 이상 세그먼트로 갈라진다
            var segsPerLine = res0.Scanlines.GroupBy(s => s.LineIndex).Select(g => g.Count()).ToList();
            bool splitOk = segsPerLine.Count == res0.LineCount && segsPerLine.All(c => c >= 2);
            double d0 = HoleDist(res0);
            // 셀 양자화(경계 셀은 점유로 집계) 여유 2셀 — 구멍 안쪽으로는 들어가지 않아야 한다
            bool holeOk = d0 >= SampleCloudGenerator.HoleRadius - 2 * prm0.GridCellMm;

            var prm4 = new GrindingParams { ToolDiameterMm = 50, OverlapPct = 30, MarginMm = 4 };
            var res4 = covSvc.Generate(prep.Points, prep.Normals, all, prm4);
            double d4 = HoleDist(res4);
            // 마진은 구멍·외곽 양쪽에서 실제로 경로를 밀어내야 한다
            bool marginOk = d4 - d0 >= prm4.MarginMm * 0.7
                            && Pts(res4).Min(p => p.X) - Pts(res0).Min(p => p.X) >= prm4.MarginMm * 0.7
                            && Pts(res0).Max(p => p.X) - Pts(res4).Max(p => p.X) >= prm4.MarginMm * 0.7;

            bool ok = splitOk && holeOk && marginOk;
            allOk &= ok;
            lines.Add($"m4 구멍패널: 라인 {res0.LineCount} 세그먼트 {res0.Scanlines.Count} " +
                      $"(라인당 {string.Join("/", segsPerLine)}) · 구멍거리 {d0:F1}mm " +
                      $"→ 마진4 {d4:F1}mm · 분할={splitOk} 마진={marginOk} {(ok ? "OK" : "FAIL")}");
        }

        // 4) 노이즈 강건성 — σ=0.15 에서 이웃 지점 법선 각도 변화(경로 요동)가
        //    무노이즈 대비 3° 이내로 늘어야 한다 (곡률 자체의 변화는 양쪽에 공통).
        {
            var prm = new GrindingParams { ToolDiameterMm = 50, OverlapPct = 30 };
            double MeanTurn(double sigma)
            {
                var sc = SampleCloudGenerator.Generate(SampleSurfaceKind.SineBump, sigma);
                var prep = prepSvc.Process(sc.Points, sc.Viewpoint);
                var res = covSvc.Generate(prep.Points, prep.Normals,
                    Enumerable.Range(0, prep.Points.Count).ToList(), prm);
                double sum = 0;
                int cnt = 0;
                foreach (var s in res.Scanlines)
                    for (int i = 1; i < s.PointNormals.Count; i++)
                    {
                        double dot = Math.Clamp(
                            Vector3D.DotProduct(s.PointNormals[i - 1], s.PointNormals[i]), -1, 1);
                        sum += Math.Acos(dot) * 180 / Math.PI;
                        cnt++;
                    }
                return cnt > 0 ? sum / cnt : 0;
            }
            double clean = MeanTurn(0.0), noisy = MeanTurn(0.15);
            bool ok = noisy - clean <= 3.0;
            allOk &= ok;
            lines.Add($"m4 노이즈 σ=0.15: 이웃 법선 변화 {clean:F2}° → {noisy:F2}° " +
                      $"(요동 +{noisy - clean:F2}°) {(ok ? "OK" : "FAIL")}");
        }

        lines.Add(allOk ? "GRINDING M4 OK" : "GRINDING M4 FAIL");
    }

    /// <summary>
    /// 전개 사상(곡면 → 평면, 거리 보존) 아래에서 잰 인접 스캔라인의 최대 표면 간격(mm).
    /// 주평면상 직선 거리와 달리 실제 미연마 폭에 해당한다.
    /// </summary>
    private static double MaxLineGapMm(CoverageResult res, Func<Point3D, Point3D> unfold)
    {
        var byLine = res.Scanlines.GroupBy(s => s.LineIndex).OrderBy(g => g.Key)
            .Select(g => g.SelectMany(s => s.PathPoints).Select(unfold).ToList()).ToList();
        double maxGap = 0;
        for (int li = 1; li < byLine.Count; li++)
        {
            var prevTree = new KdTree3(byLine[li - 1]);
            foreach (var p in byLine[li])
            {
                prevTree.Nearest(p, out double d);
                maxGap = Math.Max(maxGap, d);
            }
        }
        return maxGap;
    }

    /// <summary>스캔라인 지점 법선 vs 합성 표면 해석 법선의 평균 각도 오차(°).</summary>
    private static double MeanNormalErrorDeg(CoverageResult res, SampleCloud sc)
    {
        double sum = 0;
        int cnt = 0;
        foreach (var s in res.Scanlines)
            for (int i = 0; i < s.PathPoints.Count && i < s.PointNormals.Count; i++)
            {
                double dot = Math.Clamp(
                    Vector3D.DotProduct(sc.TrueNormalAt(s.PathPoints[i]), s.PointNormals[i]), -1, 1);
                sum += Math.Acos(dot) * 180 / Math.PI;
                cnt++;
            }
        return cnt > 0 ? sum / cnt : 0;
    }
}
