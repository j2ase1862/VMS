using System;
using System.Collections.Generic;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.DeepLearning;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 파일 · 폴더 대화상자를 VM 에서 직접 띄우지 않고 IDialogService 로 여는지 (VS-16 · VS-24 · VS-30).
    /// 대화상자 결과를 주입할 수 있어야 시험 자동화 · 녹화기 경로 주입이 된다.
    /// </summary>
    public class DialogServiceRoutingTests
    {
        [Fact]
        public void Anomaly_BrowseCalibrationFolder_UsesDialogService()
        {
            var dialogs = new FakeDialogService { FolderResult = @"D:\normal_images" };
            var tool = new AnomalyTool { CalibrationFolder = @"D:\old" };
            var vm = new AnomalyToolSettingsViewModel(tool, dialogs);

            vm.BrowseCalibrationFolderCommand.Execute(null);

            Assert.Equal(@"D:\normal_images", tool.CalibrationFolder);
            Assert.Equal(@"D:\old", dialogs.LastInitialDirectory);
        }

        [Fact]
        public void PointCloudRegistration_LoadReference_UsesDialogService()
        {
            var dialogs = new FakeDialogService { OpenFileResult = @"D:\ref\part.stl" };
            var tool = new PointCloudRegistrationTool();
            var vm = new PointCloudRegistrationToolSettingsViewModel(tool, dialogs);

            vm.LoadReferenceCommand.Execute(null);

            Assert.Equal(@"D:\ref\part.stl", tool.ReferencePath);
            Assert.Contains("CAD", vm.TrainStatus);
        }

        [Fact]
        public void PointCloudDeviation_SaveReference_Cancelled_ReportsCancel()
        {
            var dialogs = new FakeDialogService { SaveFileResult = null };
            var vm = new PointCloudDeviationToolSettingsViewModel(new PointCloudDeviationTool(), dialogs);

            vm.SaveCurrentAsReferenceCommand.Execute(null);

            Assert.Equal("저장 취소됨.", vm.TrainStatus);
            Assert.Equal(1, dialogs.SaveCalls);
        }

        [Fact]
        public void SynthData_BrowseOutputDir_UsesDialogService()
        {
            var dialogs = new FakeDialogService { FolderResult = @"D:\synth_out" };
            var vm = new SynthDataViewModel(dialogs);

            vm.BrowseOutputDirCommand.Execute(null);

            Assert.Equal(@"D:\synth_out", vm.OutputDir);
        }

        [Fact]
        public void OnnxSettings_BrowseAndSave_GoThroughViewModel()
        {
            var dialogs = new FakeDialogService { FolderResult = @"D:\trt" };
            var vm = new OnnxSettingsViewModel(dialogs,
                new OnnxSettingsService.OnnxSettings(OnnxExecutionProvider.Auto, @"D:\old_trt", true));
            OnnxSettingsService.OnnxSettings? saved = null;
            vm.SaveAction = s => saved = s;
            bool? closed = null;
            vm.CloseRequested += (_, ok) => closed = ok;

            vm.BrowseCommand.Execute(null);
            vm.Provider = OnnxExecutionProvider.Cpu;
            vm.TensorRTFp16 = false;
            vm.SaveCommand.Execute(null);

            Assert.Equal(@"D:\old_trt", dialogs.LastInitialDirectory);
            Assert.NotNull(saved);
            Assert.Equal(OnnxExecutionProvider.Cpu, saved!.Value.Provider);
            Assert.Equal(@"D:\trt", saved.Value.TensorRTCachePath);
            Assert.False(saved.Value.TensorRTFp16);
            Assert.True(closed);
        }

        [Fact]
        public void OnnxSettings_SaveFailure_ShowsErrorThroughDialogService()
        {
            var dialogs = new FakeDialogService();
            var vm = new OnnxSettingsViewModel(dialogs,
                new OnnxSettingsService.OnnxSettings(OnnxExecutionProvider.Auto, @"D:\trt", true));
            vm.SaveAction = _ => throw new InvalidOperationException("disk full");
            bool? closed = null;
            vm.CloseRequested += (_, ok) => closed = ok;

            vm.SaveCommand.Execute(null);

            Assert.Null(closed);
            Assert.Contains("disk full", dialogs.LastError);
        }

        internal sealed class FakeDialogService : IDialogService
        {
            public string? FolderResult { get; set; }
            public string? OpenFileResult { get; set; }
            public string? SaveFileResult { get; set; }
            public string? LastInitialDirectory { get; private set; }
            public string? LastError { get; private set; }
            public int SaveCalls { get; private set; }

            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) => LastError = message;
            public bool ShowConfirmation(string message, string title) => false;
            public string? ShowOpenFileDialog(string title, string filter) => OpenFileResult;
            public string[]? ShowOpenFilesDialog(string title, string filter) => null;
            public string? ShowFolderBrowserDialog(string description) => FolderResult;
            public string? ShowFolderBrowserDialog(string description, string? initialDirectory)
            {
                LastInitialDirectory = initialDirectory;
                return FolderResult;
            }
            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null)
            {
                SaveCalls++;
                return SaveFileResult;
            }
            public string? ShowRenameDialog(string currentName) => null;
            public void ShowCameraManagerDialog() { }
            public Recipe? ShowRecipeManagerDialog() => null;
            public void ShowSequenceEditorDialog(IEnumerable<SequenceDeviceEntry>? extraDevices = null) { }
            public void ShowCalibrationManagerDialog() { }
            public RecipeTemplate? ShowTemplateGalleryDialog() => null;
            public Dictionary<string, string>? ShowCameraRemapDialog(
                IReadOnlyList<(string oldId, int stepCount)> unregistered,
                IReadOnlyList<CameraInfo> cameras) => null;
            public Mat? ShowTrainMaskEditorDialog(Mat templateImage, Mat? existingMask,
                double cannyLow = 50, double cannyHigh = 150) => null;
        }
    }
}
