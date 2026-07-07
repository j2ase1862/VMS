using System;
using System.IO;
using System.Windows;
using VMS.Core.Interfaces;
using VMS.Core.Services;
using VMS.DeepLearning.Interfaces;
using VMS.DeepLearning.Services;
using VMS.DeepLearning.ViewModels;
using VMS.DeepLearning.Views;

namespace VMS.DeepLearning
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string datasetFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VMS", "Datasets");

            IAnnotationService annotationService = new AnnotationService(datasetFolder);
            ITrainingService trainingService = new TrainingService();
            ILabelingDialogService dialogService = new LabelingDialogService();
            ISamService samService = new SamService();
            IInferenceService inferenceService = new OnnxDetectionInference();

            var viewModel = new LabelingMainViewModel(annotationService, trainingService, dialogService,
                samService, inferenceService);

            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

#if DEBUG
            // 매뉴얼/문서용 컨트롤 개별 캡처 — "--capture-controls [폴더]" 인자에서만.
            // Release(배포) 빌드엔 #if DEBUG 로 이 블록과 Capture.ControlCapturer 가 컴파일되지 않는다.
            {
                static bool TryGetDir(string[] a, string flag, out string dir)
                {
                    dir = Path.Combine(Path.GetTempPath(), "VMS.DeepLearning.Capture");
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (!string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) continue;
                        if (i + 1 < a.Length && !a[i + 1].StartsWith("--")) dir = a[i + 1];
                        return true;
                    }
                    return false;
                }

                if (TryGetDir(e.Args, "--capture-controls", out string ctlDir))
                {
                    // 오프스크린 + 고정 크기 — 헤드리스에서도 확정 레이아웃으로 렌더.
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                    mainWindow.ShowInTaskbar = false;
                    mainWindow.Left = -32000; mainWindow.Top = -32000;
                    mainWindow.Width = 1400; mainWindow.Height = 900;
                    mainWindow.Show();

                    _ = mainWindow.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                        new Action(async () =>
                        {
                            try
                            {
                                await Capture.ControlCapturer.RunControlsAsync(mainWindow, viewModel, ctlDir);
                            }
                            catch (Exception ex)
                            {
                                Directory.CreateDirectory(ctlDir);
                                File.AppendAllText(Path.Combine(ctlDir, "_capture.log"), ex.ToString());
                            }
                            Shutdown();
                        }));
                    return;
                }
            }
#endif

            mainWindow.Show();
        }
    }
}
