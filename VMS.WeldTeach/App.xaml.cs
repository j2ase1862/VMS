using System.IO;
using System.Windows;
using VMS.WeldTeach.Services;
using VMS.WeldTeach.ViewModels;
using VMS.WeldTeach.Views;

namespace VMS.WeldTeach;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 서비스 수동 구성 (VMS 관례 — DI 컨테이너 없이 App 에서 조립)
        var cadKernel = new CadKernelService();
        var dialogService = new DialogService();
        var chainService = new EdgeChainService();
        var poseService = new TorchPoseService();
        var viewModel = new MainViewModel(cadKernel, dialogService, chainService, poseService);

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
                    int pickIdx = Array.IndexOf(e.Args, "--pick");
                    if (pickIdx >= 0 && pickIdx + 1 < e.Args.Length && int.TryParse(e.Args[pickIdx + 1], out int pickEdgeId))
                    {
                        viewModel.SelectEdge(pickEdgeId);
                        T($"picked #{pickEdgeId}, status={viewModel.StatusText}");
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
