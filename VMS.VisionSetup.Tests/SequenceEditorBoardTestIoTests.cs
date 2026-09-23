using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenCvSharp;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 시퀀스 편집기 단건 테스트 버튼([테스트 출력]/[테스트 읽기])의 IO 보드 경로 (2026-09-23 실증).
    /// 버튼이 노드의 장치를 보지 않고 늘 PLC 로 가서, 보드 출력 노드에서 눌러도 보드 출력이
    /// 나가지 않았다(PLC 미연결이면 "PLC가 연결되어 있지 않습니다", 연결돼 있으면 PLC 주소 "0" 에 씀).
    /// </summary>
    public class SequenceEditorBoardTestIoTests
    {
        private const string BoardId = "IoBoard_1";

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestWriteOutput_BoardNode_WritesBoardChannel_WithoutPlc(bool on)
        {
            var (vm, board) = CreateWithBoard();
            vm.SelectedNode = AddNode(vm, SequenceNodeType.OutputAction, channel: "1", bitValue: on);
            board.Bits[1] = !on;

            await vm.TestWriteOutputCommand.ExecuteAsync(null);

            Assert.Equal(on, board.Bits[1]);
            Assert.StartsWith("[테스트 출력]", vm.StatusMessage);
            Assert.DoesNotContain("PLC", vm.StatusMessage);
        }

        [Fact]
        public async Task TestWriteOutput_BoardNodeNotConnected_ReportsBoard_NotPlc()
        {
            // 보드가 장치 목록에는 있지만 연결되지 않음 — PLC 경로로 새면 안 된다
            var vm = CreateViewModel();
            vm.SelectedNode = AddNode(vm, SequenceNodeType.OutputAction, channel: "0", bitValue: true);

            await vm.TestWriteOutputCommand.ExecuteAsync(null);

            Assert.StartsWith("[테스트 출력 실패]", vm.StatusMessage);
            Assert.Contains(BoardId, vm.StatusMessage);
        }

        [Fact]
        public async Task TestWriteOutput_BoardWriteFails_ShowsFailure()
        {
            var (vm, board) = CreateWithBoard();
            board.FailWrites = true;
            vm.SelectedNode = AddNode(vm, SequenceNodeType.OutputAction, channel: "0", bitValue: true);

            await vm.TestWriteOutputCommand.ExecuteAsync(null);

            Assert.StartsWith("[테스트 출력 실패]", vm.StatusMessage);
            Assert.Contains("DO_WriteLine", vm.StatusMessage);
        }

        [Fact]
        public async Task TestReadInput_BoardNode_ReadsBoardChannel()
        {
            var (vm, board) = CreateWithBoard();
            board.Bits[0] = true;
            vm.SelectedNode = AddNode(vm, SequenceNodeType.InputCheck, channel: "0");

            await vm.TestReadInputCommand.ExecuteAsync(null);

            Assert.Equal($"[테스트 읽기] '{BoardId}' ch0 = ON", vm.StatusMessage);
        }

        // ── helpers ──

        private static (SequenceEditorViewModel vm, FakeBoard board) CreateWithBoard()
        {
            var vm = CreateViewModel();
            var board = new FakeBoard();
            vm.AttachBoardForTesting(board);
            return (vm, board);
        }

        internal static SequenceEditorViewModel CreateViewModel() => new(
            new FakeRecipeService(), new FakeCameraService(), new FakeDialogService(),
            new[]
            {
                new SequenceDeviceEntry("MainPLC", IoDeviceType.Plc),
                new SequenceDeviceEntry(BoardId, IoDeviceType.AdLinkPci743x),
            });

        private static SequenceNodeItem AddNode(SequenceEditorViewModel vm, SequenceNodeType type,
            string channel, bool? bitValue = null)
        {
            var item = new SequenceNodeItem(new SequenceNodeConfig
            {
                NodeType = type,
                Name = type.ToString(),
                DeviceId = BoardId,
                PlcAddress = channel,
                CheckMode = InputCheckMode.BitOn,
                OutputDataType = PlcDataType.Bit,
                BitValue = bitValue,
            });
            vm.Nodes.Add(item);
            return item;
        }

        private sealed class FakeBoard : IIoBoardConnection
        {
            public Dictionary<int, bool> Bits { get; } = new();
            public bool FailWrites { get; set; }

            public string DeviceId => BoardId;
            public IoDeviceType DeviceType => IoDeviceType.AdLinkPci743x;
            public bool IsConnected => true;
            public int InputChannelCount => 32;
            public int OutputChannelCount => 32;
            public event EventHandler<IoBitChangedEventArgs>? BitChanged { add { } remove { } }

            public Task<bool> ConnectAsync() => Task.FromResult(true);
            public Task DisconnectAsync() => Task.CompletedTask;
            public Task<bool> ReadBitAsync(int channel) => Task.FromResult(Bits.GetValueOrDefault(channel));
            public Task<uint> ReadPortAsync(int portNo) => Task.FromResult(0u);

            public Task WriteBitAsync(int channel, bool value)
            {
                // AdLinkDaskConnection 과 같은 실패 형태
                if (FailWrites)
                    throw new InvalidOperationException($"ADLink '{BoardId}' DO_WriteLine 실패 (ch={channel}, 에러 -1)");
                Bits[channel] = value;
                return Task.CompletedTask;
            }

            public Task WritePortAsync(int portNo, uint value) => Task.CompletedTask;
            public Task StartMonitoringAsync(int channel, int pollingIntervalMs = 50) => Task.CompletedTask;
            public Task StopMonitoringAsync(int channel) => Task.CompletedTask;
            public Task StopAllMonitoringAsync() => Task.CompletedTask;
            public void Dispose() { }
        }

        #region Service fakes

        private sealed class FakeRecipeService : IRecipeService
        {
            public Recipe? CurrentRecipe { get; set; }
            public string RecipeFolderPath => string.Empty;
            public event EventHandler<Recipe?>? CurrentRecipeChanged { add { } remove { } }
            public Recipe? LoadRecipe(string filePath) => null;
            public Recipe? ReadRecipeFile(string filePath) => null;
            public bool SaveRecipe(Recipe recipe, string? filePath = null) => true;
            public bool SaveCurrentRecipe(string? filePath = null) => true;
            public Recipe CreateNewRecipe(string? name = null) => new();
            public bool DeleteRecipe(string filePath) => true;
            public List<RecipeInfo> GetRecipeList() => new();
            public bool ExportRecipe(Recipe recipe, string exportPath) => true;
            public Recipe? ImportRecipe(string importPath) => null;
            public Recipe? DuplicateRecipe(string filePath) => null;
            public bool RenameRecipe(string filePath, string newName) => false;
            public InspectionStep? AddStep(Recipe? recipe = null, string? cameraId = null) => null;
            public void AddStep(Recipe recipe, InspectionStep step) { }
            public bool RemoveStep(Recipe? recipe, string stepId) => false;
            public bool MoveStep(Recipe? recipe, string stepId, int newSequence) => false;
            public InspectionStep? DuplicateStep(Recipe recipe, string stepId) => null;
            public InspectionStep? FindStepByRobotNode(Recipe? recipe, int nodeIndex, string? cameraId = null) => null;
            public bool AddToolToStep(InspectionStep step, ToolConfig tool) => false;
            public bool AddToolToStep(Recipe recipe, string stepId, ToolConfig tool) => false;
            public bool AddToolToStep(InspectionStep step, VisionToolBase tool) => false;
            public bool RemoveToolFromStep(InspectionStep step, string toolId) => false;
            public bool RemoveToolFromStep(Recipe recipe, string stepId, string toolId) => false;
            public List<VisionToolBase> GetToolsFromRecipe(Recipe? recipe = null) => new();
            public List<VisionToolBase> GetToolsFromStep(InspectionStep step) => new();
            public void SetCurrentRecipe(Recipe? recipe) => CurrentRecipe = recipe;
            public void CloseCurrentRecipe() => CurrentRecipe = null;
        }

        private sealed class FakeCameraService : ICameraService
        {
            public List<CameraInfo> LoadCameraRegistry() => new();
            public bool SaveCameraRegistry(List<CameraInfo>? cameras = null) => true;
            public bool AddCamera(CameraInfo camera) => true;
            public bool UpdateCamera(CameraInfo camera) => true;
            public bool RemoveCamera(string id) => true;
            public CameraInfo? GetCamera(string id) => null;
            public List<CameraInfo> GetAllCameras() => new();
            public List<CameraInfo> GetEnabledCameras() => new();
            public CameraInfo CreateNewCamera() => new();
            public string GetAppDataFolderPath() => System.IO.Path.GetTempPath();
        }

        private sealed class FakeDialogService : IDialogService
        {
            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => false;
            public string? ShowOpenFileDialog(string title, string filter) => null;
            public string[]? ShowOpenFilesDialog(string title, string filter) => null;
            public string? ShowFolderBrowserDialog(string description) => null;
            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null) => null;
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

        #endregion
    }
}
