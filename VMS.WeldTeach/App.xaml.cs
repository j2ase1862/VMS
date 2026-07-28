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

        var window = new MainWindow(viewModel);
        window.Show();
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
