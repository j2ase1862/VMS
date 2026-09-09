using System;
using System.Collections.Generic;
using System.IO;
using VMS.Core.Models.Annotation;
using VMS.Core.ViewModels;
using Xunit;

namespace VMS.Core.Tests.ViewModels
{
    /// <summary>
    /// 업로드 창의 상태 규칙. 네트워크는 건드리지 않는다 — 여기서 지키려는 것은
    /// "언제 [올리기] 가 눌리는가" 하나이고, 그게 틀리면 로그인도 안 한 채 업로드가 나가거나
    /// 이름 없는 모델 계열이 레지스트리에 생긴다.
    /// </summary>
    public class ModelUploadViewModelTests
    {
        private static string TempOnnx(long bytes = 3 * 1024 * 1024)
        {
            var path = Path.Combine(Path.GetTempPath(), "vms-upload-" + Guid.NewGuid().ToString("N") + ".onnx");
            using (var file = File.Create(path)) file.SetLength(bytes);
            return path;
        }

        private static ModelUploadViewModel Create(
            string registryUrl = "http://localhost:5310",
            string webUrl = "http://localhost:5000",
            string? onnxPath = null,
            DatasetTaskType taskType = DatasetTaskType.Detection,
            IReadOnlyList<string>? classes = null,
            string suggestedName = "로고 검출")
            => new(registryUrl, webUrl, onnxPath ?? TempOnnx(), taskType,
                   classes ?? new[] { "logo", "scratch" }, suggestedName);

        [Fact]
        public void Summary_DescribesWhatIsBeingUploaded()
        {
            var path = TempOnnx(3 * 1024 * 1024);
            try
            {
                var vm = Create(onnxPath: path, taskType: DatasetTaskType.Segmentation);
                Assert.Equal(Path.GetFileName(path), vm.FileName);
                Assert.Equal("3 MB", vm.FileSizeText);
                Assert.Equal("세그멘테이션", vm.TaskTypeText);
                Assert.Equal("logo, scratch", vm.ClassesText);
                Assert.Equal("로고 검출", vm.NewModelName);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void FileSize_SaysSoWhenFileIsGone()
            => Assert.Equal("파일 없음", Create(onnxPath: @"C:\없는경로\없는파일.onnx").FileSizeText);

        [Fact]
        public void Classes_MayBeEmpty()
            => Assert.Equal("클래스 없음", Create(classes: Array.Empty<string>()).ClassesText);

        /// <summary>주소가 없으면 창을 띄우기 전에 막고, 무엇이 빠졌는지 말해 준다.</summary>
        [Theory]
        [InlineData("", "", "MLOps 서버 주소와 웹 서버 주소")]
        [InlineData("", "http://localhost:5000", "MLOps 서버 주소가")]
        [InlineData("http://localhost:5310", "", "웹 서버 주소가")]
        public void IsConfigured_IsFalseWhenAnUrlIsMissing(string registry, string web, string hint)
        {
            var vm = Create(registryUrl: registry, webUrl: web);
            Assert.False(vm.IsConfigured);
            Assert.Contains(hint, vm.ConfigurationHint);
        }

        [Fact]
        public void IsConfigured_IsTrueWhenBothUrlsArePresent()
            => Assert.True(Create().IsConfigured);

        /// <summary>로그인 전에는 올릴 수 없다. 등록은 사람의 계정으로만 한다.</summary>
        [Fact]
        public void CanUpload_IsFalseBeforeSignIn()
        {
            var vm = Create();
            Assert.False(vm.CanUpload);
            Assert.True(vm.ShowSignIn);
        }

        [Fact]
        public void CanUpload_NeedsAName_WhenCreatingANewModel()
        {
            var vm = Create();
            vm.SignedIn = true;
            vm.CreateNew = true;

            vm.NewModelName = "   ";
            Assert.False(vm.CanUpload);

            vm.NewModelName = "로고 검출";
            Assert.True(vm.CanUpload);
        }

        [Fact]
        public void CanUpload_NeedsASelection_WhenAddingToAnExistingModel()
        {
            var vm = Create();
            vm.SignedIn = true;
            vm.UseExisting = true;

            Assert.False(vm.CanUpload);

            vm.SelectedModel = new ModelUploadViewModel.ModelChoice(Guid.NewGuid(), "로고 검출", "logo");
            Assert.True(vm.CanUpload);
        }

        /// <summary>한 번 올린 뒤에는 다시 눌러도 같은 파일이 두 번 올라가지 않는다.</summary>
        [Fact]
        public void CanUpload_IsFalseOnceUploaded()
        {
            var vm = Create();
            vm.SignedIn = true;
            vm.NewModelName = "로고 검출";
            Assert.True(vm.CanUpload);

            vm.Result = "v1 로 등록했습니다";
            Assert.False(vm.CanUpload);
            Assert.True(vm.HasResult);
        }

        [Fact]
        public void CanUpload_IsFalseWhileBusy()
        {
            var vm = Create();
            vm.SignedIn = true;
            vm.NewModelName = "로고 검출";

            vm.Busy = true;
            Assert.False(vm.CanUpload);

            vm.Busy = false;
            Assert.True(vm.CanUpload);
        }

        /// <summary>라디오 두 개가 서로를 되돌려 놓지 않아야 한다 — 켜지는 쪽만 값을 넘긴다.</summary>
        [Fact]
        public void UseExisting_IsTheOppositeOfCreateNew()
        {
            var vm = Create();
            vm.CreateNew = true;
            Assert.False(vm.UseExisting);

            vm.UseExisting = true;
            Assert.False(vm.CreateNew);

            // 꺼지는 쪽은 아무것도 하지 않는다 (다른 라디오가 켜지며 정한다)
            vm.UseExisting = false;
            Assert.False(vm.CreateNew);
        }

        [Fact]
        public void Status_And_Error_TrackTheirText()
        {
            var vm = Create();
            Assert.False(vm.HasStatus);
            Assert.False(vm.HasError);

            vm.Status = "올리는 중…";
            vm.Error = "권한이 없습니다";
            Assert.True(vm.HasStatus);
            Assert.True(vm.HasError);
        }
    }
}
