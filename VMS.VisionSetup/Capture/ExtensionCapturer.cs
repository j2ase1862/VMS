#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VMS.Core.Services;

namespace VMS.VisionSetup.Capture
{
    /// <summary>
    /// 사내 매뉴얼 5.13 (확장 옵션 화면) 그림 — "--capture-extension [폴더]" (2026-11-11).
    /// 화면 밖에 창을 띄워 앱이 직접 렌더한다(실제 마우스 · 키보드 입력 없음). Release 빌드에는 컴파일되지 않는다.
    ///   env VMS_CAPTURE_RECIPE = 3D 카메라 스텝이 있는 레시피(사본), VMS_CAPTURE_STEP = 그 스텝 인덱스
    /// 만드는 그림:
    ///   Ext_CameraSettings3D.png  스텝 선택 상태(Camera Settings 의 3D 스캔 묶음)
    ///   Ext_WaypointSteps.png     웨이포인트 스텝을 만든 뒤(Steps 목록 · Robot MultiView 줄)
    ///   MultiViewSetup.png · HandEyeCalibration.png · RegistryPicker_stage.png · RegistryPicker_pinned.png
    /// 레지스트리 창은 MLOps 서버에 닿지 않도록 가짜 목록으로 채운다. 레시피는 메모리에서만 바꾸고 저장하지 않는다.
    /// </summary>
    internal static partial class ControlCapturer
    {
        public static async Task RunExtensionAsync(Window mainView, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string log = Path.Combine(outputDir, "_extension.log");
            if (mainView.DataContext is not ViewModels.MainViewModel vm) { File.AppendAllText(log, "MainViewModel 없음\n"); return; }

            double w = double.IsNaN(mainView.Width) || mainView.Width < 1 ? 1920 : mainView.Width;
            double h = double.IsNaN(mainView.Height) || mainView.Height < 1 ? 1080 : mainView.Height;
            var size = new Size(w, h);

            // ① 3D 스텝 선택 — Camera Settings 의 3D 스캔 묶음
            try
            {
                var recipePath = Environment.GetEnvironmentVariable("VMS_CAPTURE_RECIPE");
                if (!string.IsNullOrEmpty(recipePath) && File.Exists(recipePath))
                {
                    var recipe = Services.RecipeService.Instance.LoadRecipe(recipePath);
                    if (recipe != null)
                    {
                        Services.RecipeService.Instance.SetCurrentRecipe(recipe);
                        int stepIdx = int.TryParse(Environment.GetEnvironmentVariable("VMS_CAPTURE_STEP"), out var si) ? si : 0;
                        if (stepIdx >= 0 && stepIdx < recipe.Steps.Count) vm.SelectedStep = recipe.Steps[stepIdx];
                        vm.SelectedTool = null;
                        await LayoutPass(mainView, size);
                        await LayoutPass(mainView, size);
                        RenderMain(mainView, w, h, Path.Combine(outputDir, "Ext_CameraSettings3D.png"));

                        // ② 웨이포인트 스텝 만들기 (메모리에서만)
                        if (vm.GenerateWaypointsCommand.CanExecute(null))
                        {
                            vm.GenerateWaypointsCommand.Execute(null);
                            await LayoutPass(mainView, size);
                            await LayoutPass(mainView, size);
                            RenderMain(mainView, w, h, Path.Combine(outputDir, "Ext_WaypointSteps.png"));
                        }
                        else File.AppendAllText(log, "GenerateWaypoints 실행 불가 (카메라 선택 없음)\n");
                    }
                }
                else File.AppendAllText(log, "VMS_CAPTURE_RECIPE 없음 — 3D 스텝 · 웨이포인트 그림 건너뜀\n");
            }
            catch (Exception ex) { File.AppendAllText(log, "3D/waypoint: " + ex + "\n"); }

            // ③ 멀티뷰 · 핸드아이 창
            var wins = new List<(string, Window, bool)>();
            try { wins.Add(("MultiViewSetup", new Views.MultiViewSetupWindow { DataContext = vm }, false)); }
            catch (Exception ex) { File.AppendAllText(log, "MultiView ctor: " + ex + "\n"); }
            try
            {
                // 생성자는 서비스를 저장만 한다 — 그림만 찍으므로 장비가 없어도(null) 창은 그린다. 버튼은 누르지 않는다.
                wins.Add(("HandEyeCalibration", new Views.HandEyeCalibrationWindow
                {
                    DataContext = new ViewModels.HandEyeCalibrationViewModel(vm.RobotService!, vm.CameraAcquisition!, vm.DialogServiceAccessor)
                }, false));
            }
            catch (Exception ex) { File.AppendAllText(log, "HandEye ctor: " + ex + "\n"); }
            await RunWindowsFullAsync(wins, outputDir, mainView);

            // ④ 레지스트리에서 모델 고르기 — 가짜 목록 (서버에 닿지 않음)
            try { await CaptureRegistryPickerAsync(mainView, outputDir); }
            catch (Exception ex) { File.AppendAllText(log, "RegistryPicker: " + ex + "\n"); }
        }

        private static void RenderMain(Window mainView, double w, double h, string path)
        {
            var root = mainView.Content as FrameworkElement ?? mainView;
            double rw = root.ActualWidth > 1 ? root.ActualWidth : w;
            double rh = root.ActualHeight > 1 ? root.ActualHeight : h;
            RenderRootToFile(mainView, root, rw, rh, path);
        }

        private static async Task CaptureRegistryPickerAsync(Window owner, string outputDir)
        {
            var fitting = new RegistryModel { Id = Guid.Parse("28786c77-426e-48be-8b1f-347f9e2c9a6b"), Name = "피팅 검출", TaskType = "detection", Classes = new[] { "T", "Y" } };
            var models = new List<RegistryModel>
            {
                fitting,
                new RegistryModel { Id = Guid.Parse("92c4facb-b4e3-4b3a-8c61-62dc8012b1e7"), Name = "라인A 스크래치 검출", TaskType = "detection", Classes = new[] { "scratch", "dent" } },
            };
            var now = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
            var versions = new List<RegistryModelVersion>
            {
                new RegistryModelVersion { Id = Guid.NewGuid(), Number = 3, Stage = "candidate", SizeBytes = 41_000_000, CreatedAt = now },
                new RegistryModelVersion { Id = Guid.NewGuid(), Number = 2, Stage = "production", SizeBytes = 41_000_000, CreatedAt = now.AddDays(-6) },
                new RegistryModelVersion { Id = Guid.NewGuid(), Number = 1, Stage = "retired", SizeBytes = 40_000_000, CreatedAt = now.AddDays(-20) },
            };
            var pvm = new ViewModels.ModelRegistryPickerViewModel(
                (type, ct) => Task.FromResult<IReadOnlyList<RegistryModel>>(models),
                (id, ct) => Task.FromResult<IReadOnlyList<RegistryModelVersion>>(id == fitting.Id ? versions : new List<RegistryModelVersion>()),
                "detection");
            var win = new Views.Dialogs.ModelRegistryPickerWindow(pvm)
            {
                Owner = owner, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000,
            };
            double w = double.IsNaN(win.Width) ? 760 : win.Width, h = double.IsNaN(win.Height) ? 560 : win.Height;
            bool standard = win.WindowStyle != WindowStyle.None;
            win.Show();
            try
            {
                await LayoutWindow(win, standard, w, h);
                for (int i = 0; i < 40 && pvm.Models.Count == 0; i++) await Task.Delay(50);
                pvm.SelectedModel = pvm.Models.FirstOrDefault();
                if (pvm.PendingVersionsLoad != null) await pvm.PendingVersionsLoad;
                await LayoutWindow(win, standard, w, h);
                var root = win.Content as FrameworkElement ?? win;
                RenderRootToFile(win, root, Math.Max(root.ActualWidth, w), Math.Max(root.ActualHeight, h), Path.Combine(outputDir, "RegistryPicker_stage.png"));

                pvm.PinVersion = true;
                pvm.SelectedVersion = pvm.Versions.FirstOrDefault(v => v.Version.Stage == "production") ?? pvm.Versions.FirstOrDefault();
                await LayoutWindow(win, standard, w, h);
                RenderRootToFile(win, root, Math.Max(root.ActualWidth, w), Math.Max(root.ActualHeight, h), Path.Combine(outputDir, "RegistryPicker_pinned.png"));
            }
            finally { try { win.Close(); } catch { } }
        }
    }
}
#endif
