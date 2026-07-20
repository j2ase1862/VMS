using System;
using System.IO;
using System.Linq;
using VMS.Camera.Models;
using VMS.Interfaces;
using VMS.Models;
using VMS.PLC.Models;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// CameraViewModel.SavePointCloudCommand 검증:
    /// - CanExecute: 3D 데이터 없음 / Live 그랩 중에는 비활성
    /// - 저장 파일은 VisionSetup 이 사용하는 PointCloudData.LoadFromFile 로 왕복 로드 가능 (.vpc)
    /// - 다이얼로그 취소 시 파일 미생성
    /// - 기본 파일명은 카메라 이름의 경로 금지 문자를 제거
    /// </summary>
    public class CameraViewModelSavePointCloudTests : IDisposable
    {
        private readonly string _tempDir;

        public CameraViewModelSavePointCloudTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"vpc_save_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private static PointCloudData MakeCloud(string name = "TestCloud")
        {
            var xyz = new float[] { 0f, 0f, 0f, 1f, 2f, 3f, -1.5f, 0.5f, 10f };
            var rgb = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255 };
            return PointCloudData.FromArrays(xyz, rgb, name, width: 3, height: 1);
        }

        private CameraViewModel MakeVm(FakeDialogService dialog, string cameraName = "Cam1")
            => new(dialog, new FakeConfigurationService()) { Name = cameraName };

        [Fact]
        public void CanExecute_is_false_without_point_cloud()
        {
            var vm = MakeVm(new FakeDialogService());

            Assert.False(vm.SavePointCloudCommand.CanExecute(null));
        }

        [Fact]
        public void CanExecute_is_true_with_point_cloud_and_false_while_live_grabbing()
        {
            var vm = MakeVm(new FakeDialogService());
            vm.CurrentPointCloud = MakeCloud();

            Assert.True(vm.SavePointCloudCommand.CanExecute(null));

            vm.IsLiveGrabbing = true;
            Assert.False(vm.SavePointCloudCommand.CanExecute(null));

            vm.IsLiveGrabbing = false;
            Assert.True(vm.SavePointCloudCommand.CanExecute(null));
        }

        [Fact]
        public void Save_round_trips_through_LoadFromFile()
        {
            var path = Path.Combine(_tempDir, "grab.vpc");
            var vm = MakeVm(new FakeDialogService { SavePath = path });
            var cloud = MakeCloud("GrabbedCloud");
            vm.CurrentPointCloud = cloud;

            vm.SavePointCloudCommand.Execute(null);

            Assert.True(File.Exists(path));
            Assert.Contains("saved", vm.ResultMessage);

            using var loaded = PointCloudData.LoadFromFile(path);
            Assert.Equal(cloud.PointCount, loaded.PointCount);
            Assert.Equal(cloud.GridWidth, loaded.GridWidth);
            Assert.Equal(cloud.GridHeight, loaded.GridHeight);
            Assert.Equal(cloud.Positions.Take(cloud.PointCount), loaded.Positions.Take(loaded.PointCount));
        }

        [Fact]
        public void Dialog_cancel_saves_nothing()
        {
            var dialog = new FakeDialogService { SavePath = null };
            var vm = MakeVm(dialog);
            vm.CurrentPointCloud = MakeCloud();

            vm.SavePointCloudCommand.Execute(null);

            Assert.Equal(1, dialog.SaveDialogCalls);
            Assert.Empty(Directory.GetFiles(_tempDir));
        }

        [Fact]
        public void Default_file_name_strips_invalid_path_chars_from_camera_name()
        {
            var dialog = new FakeDialogService { SavePath = null };
            var vm = MakeVm(dialog, cameraName: "Cam:1/Line*2");
            vm.CurrentPointCloud = MakeCloud();

            vm.SavePointCloudCommand.Execute(null);

            Assert.NotNull(dialog.LastSuggestedFileName);
            Assert.StartsWith("Cam_1_Line_2_", dialog.LastSuggestedFileName);
            Assert.True(dialog.LastSuggestedFileName!.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        }

        // ─── Fakes ──────────────────────────────────────────────

        private sealed class FakeDialogService : IDialogService
        {
            public string? SavePath { get; set; }
            public int SaveDialogCalls { get; private set; }
            public string? LastSuggestedFileName { get; private set; }

            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null)
            {
                SaveDialogCalls++;
                LastSuggestedFileName = fileName;
                return SavePath;
            }

            public string? ShowOpenFileDialog(string filter, string defaultExt) => null;
            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => false;
            public bool ShowLoginDialog(IUserService userService) => false;
        }

        private sealed class FakeConfigurationService : IConfigurationService
        {
            public string ConfigDirectory => Path.GetTempPath();
            public SystemConfiguration LoadSystemConfiguration() => new();
            public LayoutConfiguration LoadLayoutConfiguration() => new();
            public bool SaveLayoutConfiguration(LayoutConfiguration config) => true;
            public bool SaveSystemConfiguration(SystemConfiguration config) => true;
            public PlcSignalConfiguration LoadPlcSignalConfiguration() => new();
            public bool SavePlcSignalConfiguration(PlcSignalConfiguration config) => true;
        }
    }
}
