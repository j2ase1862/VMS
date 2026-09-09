using System;
using VMS.Core.Models.Annotation;
using VMS.Core.Services;
using VMS.Core.ViewModels;
using Xunit;

namespace VMS.Core.Tests.ViewModels
{
    /// <summary>
    /// 내려받기 창의 상태 규칙. 네트워크는 건드리지 않는다 — 여기서 지키려는 것은
    /// "언제 [받기] 와 [새 판 뜨기] 가 눌리는가" 이고, 그게 틀리면 판을 고르지도 않은 채
    /// 받기가 나가거나, 이미 받아 놓은 폴더를 한 번 더 지우고 다시 받는다.
    /// </summary>
    public class DatasetDownloadViewModelTests
    {
        private static DatasetDownloadViewModel Create(
            string serverUrl = "http://localhost:5310",
            string webUrl = "http://localhost:5000",
            DatasetTaskType taskType = DatasetTaskType.Detection,
            string target = @"D:\datasets\web")
            => new(serverUrl, webUrl, taskType, target);

        private static DatasetDownloadViewModel.VersionChoice AnyVersion() =>
            new(new RegistryDatasetVersion
            {
                Id = Guid.NewGuid(),
                Name = "2026-09-09 스냅샷",
                ExportFormat = "yolo",
                ManifestHash = "abcdef0123456789",
                SizeBytes = 5 * 1024 * 1024,
                ImageCount = 120,
                AnnotationCount = 340,
            }, "2026-09-09 스냅샷", "yolo · 이미지 120장");

        [Fact]
        public void TaskType_IsShownInKorean()
            => Assert.Equal("이상 탐지", Create(taskType: DatasetTaskType.AnomalyDetection).TaskTypeText);

        [Theory]
        [InlineData("", "", "MLOps 서버 주소와 웹 서버 주소")]
        [InlineData("", "http://localhost:5000", "MLOps 서버 주소가")]
        [InlineData("http://localhost:5310", "", "웹 서버 주소가")]
        public void IsConfigured_IsFalseWhenAnUrlIsMissing(string server, string web, string hint)
        {
            var vm = Create(serverUrl: server, webUrl: web);
            Assert.False(vm.IsConfigured);
            Assert.Contains(hint, vm.ConfigurationHint);
        }

        [Fact]
        public void CanDownload_IsFalseBeforeSignIn()
        {
            var vm = Create();
            Assert.False(vm.CanDownload);
            Assert.False(vm.CanSnapshot);
            Assert.True(vm.ShowSignIn);
        }

        /// <summary>판을 고르지 않으면 받을 것이 없다.</summary>
        [Fact]
        public void CanDownload_NeedsAVersion()
        {
            var vm = Create();
            vm.SignedIn = true;
            Assert.False(vm.CanDownload);

            vm.SelectedVersion = AnyVersion();
            Assert.True(vm.CanDownload);
        }

        /// <summary>풀어 넣을 곳이 없으면 받을 수 없다 — 어디에 풀지 정하지 않고 시작하면 안 된다.</summary>
        [Fact]
        public void CanDownload_NeedsATargetDirectory()
        {
            var vm = Create();
            vm.SignedIn = true;
            vm.SelectedVersion = AnyVersion();

            vm.TargetDirectory = "   ";
            Assert.False(vm.CanDownload);

            vm.TargetDirectory = @"D:\datasets\web";
            Assert.True(vm.CanDownload);
        }

        /// <summary>새 판 뜨기는 데이터셋만 있으면 된다 — 판이 하나도 없을 때 쓰는 버튼이다.</summary>
        [Fact]
        public void CanSnapshot_NeedsADatasetOnly()
        {
            var vm = Create();
            vm.SignedIn = true;
            Assert.False(vm.CanSnapshot);

            vm.SelectedDataset = new DatasetDownloadViewModel.DatasetChoice(
                Guid.NewGuid(), "로고 데이터셋", "object, logo", "detection");
            Assert.True(vm.CanSnapshot);
        }

        /// <summary>한 번 받은 뒤에는 다시 눌리지 않는다. 받은 폴더를 지우고 다시 받는 일이 없어야 한다.</summary>
        [Fact]
        public void Commands_AreDisabledOnceDownloaded()
        {
            var vm = Create();
            vm.SignedIn = true;
            // 데이터셋을 먼저 고른다 — 데이터셋을 바꾸면 고른 판이 버려진다 (다른 데이터셋의 판을
            // 받는 것을 막기 위한 것이라, 순서가 뒤집히면 판이 사라진다)
            vm.SelectedDataset = new DatasetDownloadViewModel.DatasetChoice(
                Guid.NewGuid(), "로고 데이터셋", "object, logo", "detection");
            vm.SelectedVersion = AnyVersion();
            Assert.True(vm.CanDownload);
            Assert.True(vm.CanSnapshot);

            vm.DownloadedPath = @"D:\datasets\web\로고-abcdef01";
            Assert.False(vm.CanDownload);
            Assert.False(vm.CanSnapshot);
            Assert.True(vm.HasResult);
        }

        /// <summary>
        /// 데이터셋을 바꾸면 고른 판을 버린다. 그대로 두면 다른 데이터셋의 판을 받는다 —
        /// 폴더는 만들어지고 학습은 돌지만, 라벨이 통째로 다른 것이 된다.
        /// </summary>
        [Fact]
        public void ChangingTheDataset_DropsTheChosenVersion()
        {
            var vm = Create();
            vm.SignedIn = true;
            vm.SelectedVersion = AnyVersion();
            Assert.NotNull(vm.SelectedVersion);

            vm.SelectedDataset = new DatasetDownloadViewModel.DatasetChoice(
                Guid.NewGuid(), "다른 데이터셋", "scratch", "detection");

            Assert.Null(vm.SelectedVersion);
            Assert.Empty(vm.Versions);
            Assert.False(vm.CanDownload);
        }

        [Fact]
        public void Commands_AreDisabledWhileBusy()
        {
            var vm = Create();
            vm.SignedIn = true;
            vm.SelectedVersion = AnyVersion();

            vm.Busy = true;
            Assert.False(vm.CanDownload);

            vm.Busy = false;
            Assert.True(vm.CanDownload);
        }

        [Fact]
        public void Status_And_Error_TrackTheirText()
        {
            var vm = Create();
            Assert.False(vm.HasStatus);
            Assert.False(vm.HasError);

            vm.Status = "받는 중…";
            vm.Error = "서버에 닿지 못했습니다";
            Assert.True(vm.HasStatus);
            Assert.True(vm.HasError);
        }

        /// <summary>전체 크기를 모르면 진행률은 0 이다 — 100 으로 보이면 다 받은 줄 안다.</summary>
        [Theory]
        [InlineData(0L, 0L, 0.0)]
        [InlineData(50L, 100L, 50.0)]
        [InlineData(100L, 100L, 100.0)]
        [InlineData(150L, 100L, 100.0)]
        public void Progress_IsClamped(long received, long total, double expected)
            => Assert.Equal(expected, new DatasetDownloadProgress(received, total, "").Percent, 3);
    }
}
