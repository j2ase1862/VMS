using System;
using System.IO;
using OpenCvSharp;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// BlobTool 형상 필터(원형도·볼록도) — 설정 화면 노출과 동작.
    ///
    /// MinCircularity·MaxCircularity·MinConvexity 는 필터에 적용되는데 설정 화면(XAML)에 칸이 없어,
    /// 레시피에 저장된 값이 판정을 좌우해도 화면에서 보거나 고칠 수 없었다 (2026-10-12 GS 시험자료 Blob 레시피).
    /// </summary>
    public class BlobToolShapeFilterTests
    {
        // 흰 배경 위 검은 원 2개 — 왼쪽은 온전한 원, 오른쪽은 60° 부채꼴을 잘라낸 결손 원
        private static Mat MakeCircleAndNotchedCircle()
        {
            var img = new Mat(200, 400, MatType.CV_8UC1, Scalar.White);
            Cv2.Circle(img, new Point(100, 100), 50, Scalar.Black, -1);
            Cv2.Circle(img, new Point(300, 100), 50, Scalar.Black, -1);
            Cv2.Ellipse(img, new Point(300, 100), new Size(51, 51), 0, -30, 30, Scalar.White, -1);
            return img;
        }

        private static BlobTool NewTool() => new()
        {
            SegmentationPolarity = SegmentationPolarity.DarkOnLight,
            UseCountJudgment = true,
            ExpectedCount = 2,
        };

        [Fact]
        public void MinCircularity_ExcludesNotchedCircle_FromCount()
        {
            using var img = MakeCircleAndNotchedCircle();

            var noFilter = NewTool().Execute(img);
            Assert.Equal(2, noFilter.Data["BlobCount"]);
            Assert.True(noFilter.Success);

            var tool = NewTool();
            tool.MinCircularity = 0.8;
            var filtered = tool.Execute(img);
            Assert.Equal(1, filtered.Data["BlobCount"]);
            Assert.False(filtered.Success);   // 결손 원이 빠져 개수 1 ≠ 2
        }

        [Fact]
        public void MinConvexity_ExcludesNotchedCircle_FromCount()
        {
            using var img = MakeCircleAndNotchedCircle();
            var tool = NewTool();
            tool.MinConvexity = 0.95;
            var r = tool.Execute(img);
            Assert.Equal(1, r.Data["BlobCount"]);
        }

        [Fact]
        public void SettingsViewModel_ShapeFilter_WiredToTool()
        {
            var tool = new BlobTool();
            var vm = new BlobToolSettingsViewModel(tool);

            vm.MinCircularity = 0.8;
            vm.MaxCircularity = 0.95;
            vm.MinConvexity = 0.9;

            // VM 입력이 툴까지 도달해야 저장(SerializeTool)에 반영된다
            Assert.Equal(0.8, tool.MinCircularity, 6);
            Assert.Equal(0.95, tool.MaxCircularity, 6);
            Assert.Equal(0.9, tool.MinConvexity, 6);

            // 역방향 — 레시피에서 읽은 값이 화면에 보여야 한다
            tool.MinCircularity = 0.7;
            Assert.Equal(0.7, vm.MinCircularity, 6);
        }

        [Fact]
        public void ShapeFilter_SurvivesSerializerRoundTrip()
        {
            var tool = new BlobTool { MinCircularity = 0.8, MaxCircularity = 0.95, MinConvexity = 0.9 };
            var cfg = ToolSerializer.SerializeTool(tool);
            var back = Assert.IsType<BlobTool>(ToolSerializer.DeserializeTool(cfg));
            Assert.Equal(0.8, back.MinCircularity, 6);
            Assert.Equal(0.95, back.MaxCircularity, 6);
            Assert.Equal(0.9, back.MinConvexity, 6);
        }

        [Theory]
        [InlineData("MinCircularity")]
        [InlineData("MaxCircularity")]
        [InlineData("MinConvexity")]
        public void SettingsXaml_ExposesShapeFilter(string property)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VMS.VisionSetup", "VMS.VisionSetup.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "VMS.VisionSetup", "Views", "ToolSettings", "Tools", "BlobToolSettings.xaml"));
            Assert.Contains($"{{Binding {property}}}", xaml);
            Assert.Contains($"ParameterName=\"{property}\"", xaml);
        }
    }
}
