using System;
using System.IO;
using System.Linq;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.DeepLearning;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 제출 매뉴얼 대조(2026-11-22)에서 화면 쪽을 고친 것들 — 다시 어긋나지 않게 묶어 둔다.
    /// </summary>
    public class ManualAuditScreenTextTests
    {
        private static string ReadSettingsXaml(string fileName)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "VMS.VisionSetup");
                if (File.Exists(Path.Combine(candidate, "VMS.VisionSetup.csproj")))
                    return File.ReadAllText(Path.Combine(candidate, "Views", "ToolSettings", "Tools", fileName));
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("VMS.VisionSetup 소스 폴더를 찾지 못했습니다.");
        }

        // ── Class Names 직접 입력 (매뉴얼 §6.11: 메타데이터가 없으면 클래스 이름을 쉼표로 입력) ──

        [Theory]
        [InlineData("DetectionToolSettings.xaml")]
        [InlineData("ClassifyToolSettings.xaml")]
        public void 설정_화면에_Class_Names_입력_칸이_있다(string fileName)
        {
            // ClassNamesText 는 도구 · 저장 · 설정 VM 에 있었지만 화면에 묶인 칸이 없어 넣을 길이 없었다.
            Assert.Contains("Text=\"{Binding ClassNamesText", ReadSettingsXaml(fileName));
        }

        [Fact]
        public void Detection_클래스_이름을_입력하면_NG_Classes_를_고를_수_있다()
        {
            var vm = new DetectionToolSettingsViewModel(new DetectionTool());
            Assert.False(vm.HasClassNames);

            vm.ClassNamesText = "good, scratch, dent";

            Assert.Equal(new[] { "good", "scratch", "dent" }, vm.NgClassOptions.Select(o => o.Name).ToArray());
            Assert.True(vm.HasClassNames);
        }

        [Fact]
        public void Classify_클래스_이름을_입력하면_OK_Classes_를_고를_수_있다()
        {
            var vm = new ClassifyToolSettingsViewModel(new ClassifyTool());
            Assert.False(vm.HasClassNames);

            vm.ClassNamesText = "good, scratch, dent";

            Assert.Equal(new[] { "good", "scratch", "dent" }, vm.OkClassOptions.Select(o => o.Name).ToArray());
            Assert.True(vm.HasClassNames);
        }

        // ── 물음표 도움말의 선택지가 화면 콤보와 같다 ──

        [Fact]
        public void Blur_Type_도움말은_화면의_선택지만_설명한다()
        {
            var help = HelpContent.GetParameterHelp("BlurTool", "BlurType");

            Assert.False(string.IsNullOrWhiteSpace(help));
            foreach (var name in Enum.GetNames(typeof(BlurType)))
                Assert.Contains($"• {name}:", help);
            Assert.DoesNotContain("Box", help);   // 선택지에 없는 이름이 적혀 있었다
        }

        [Fact]
        public void Line_Fit_Fit_Method_도움말은_화면의_선택지만_설명한다()
        {
            var help = HelpContent.GetParameterHelp("LineFitTool", "FitMethod");

            Assert.False(string.IsNullOrWhiteSpace(help));
            foreach (var name in Enum.GetNames(typeof(LineFitMethod)))
                Assert.Contains($"• {name}:", help);
            Assert.DoesNotContain("Huber", help);   // 선택지에 없는 방법이 적혀 있었다
            Assert.Equal(Enum.GetNames(typeof(LineFitMethod)).Length, help!.Split('•').Length - 1);
        }
    }
}
