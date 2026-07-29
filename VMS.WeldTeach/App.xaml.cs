using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
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
            cloudService, icpService);

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
            File.WriteAllLines(log, lines);
        }
        catch (Exception ex)
        {
            File.WriteAllText(log, "SELFTEST FAIL: " + ex);
        }
    }
}
