using System.Linq;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// RF-DETR-seg 도구가 화면과 레시피에 제대로 물렸는지.
    ///
    /// <para>
    /// 이 배선은 빠져도 빌드가 통과한다. 빠진 자리마다 드러나는 모습이 다르고 전부 조용하다 —
    /// 팩토리가 빠지면 레시피를 열 때 도구가 사라지고, 직렬화가 빠지면 파라미터가 저장되지 않고,
    /// 설정 VM 의 래퍼가 빠지면 그 칸만 비어 보이고, 도움말이 빠지면 도움말 패널이 빈다.
    /// </para>
    /// </summary>
    public class RfdetrSegWiringTests
    {
        private const string ToolType = "RfdetrSegTool";

        [Fact]
        public void Appears_in_the_deep_learning_category()
        {
            var tools = VisionService.GetAvailableTools();
            Assert.True(tools.ContainsKey("Deep Learning"));
            Assert.Contains(ToolType, tools["Deep Learning"]);
        }

        [Fact]
        public void Has_a_display_name()
        {
            var name = VisionService.GetToolDisplayName(ToolType);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.NotEqual(ToolType, name);   // 타입 이름을 그대로 보여 주면 안 된다
        }

        /// <summary>
        /// 도구 고유 파라미터가 왕복해야 한다. 베이스 프로퍼티만 보는 회귀 시험은
        /// 직렬화 case 가 통째로 빠져도 통과한다 (default 가 "담을 수 있는 것만 담기" 라서).
        /// </summary>
        [Fact]
        public void RoundTrips_its_own_parameters()
        {
            var original = new RfdetrSegTool
            {
                ModelPath = @"D:\models\seg.onnx",
                InputSize = 336,
                ConfidenceThreshold = 0.42f,
                MaxInstances = 37,
                ShowOverlay = false,
                OverlayOpacity = 0.75,
                DrawBoxes = false,
                OutputMaskImage = true,
            };

            var config = ToolSerializer.SerializeTool(original);
            var restored = Assert.IsType<RfdetrSegTool>(ToolSerializer.DeserializeTool(config!));

            Assert.Equal(original.ModelPath, restored.ModelPath);
            Assert.Equal(original.InputSize, restored.InputSize);
            Assert.Equal(original.ConfidenceThreshold, restored.ConfidenceThreshold);
            Assert.Equal(original.MaxInstances, restored.MaxInstances);
            Assert.Equal(original.ShowOverlay, restored.ShowOverlay);
            Assert.Equal(original.OverlayOpacity, restored.OverlayOpacity);
            Assert.Equal(original.DrawBoxes, restored.DrawBoxes);
            Assert.Equal(original.OutputMaskImage, restored.OutputMaskImage);
        }

        /// <summary>
        /// 자매 도구도 같은 자리에서 새고 있었다 — 직렬화는 담는데 역직렬화가 되읽지 않아,
        /// 레시피를 다시 열면 마스크 출력이 꺼져 있었다.
        /// </summary>
        [Fact]
        public void YoloSeg_restores_its_mask_output_too()
        {
            var original = new YoloSegTool { ModelPath = @"D:\models\y.onnx", OutputMaskImage = true };

            var config = ToolSerializer.SerializeTool(original);
            var restored = Assert.IsType<YoloSegTool>(ToolSerializer.DeserializeTool(config!));

            Assert.True(restored.OutputMaskImage);
        }

        /// <summary>
        /// 설정 화면의 래퍼 속성이 빠지면 그 칸만 조용히 비어 보인다.
        /// 도구가 내놓는 값과 화면이 읽는 값이 같은지 본다.
        /// </summary>
        [Fact]
        public void Settings_view_model_exposes_every_parameter()
        {
            var tool = new RfdetrSegTool
            {
                ModelPath = @"D:\models\seg.onnx",
                InputSize = 336,
                ConfidenceThreshold = 0.42f,
                MaxInstances = 37,
                OverlayOpacity = 0.75,
                OutputMaskImage = true,
            };
            var vm = new RfdetrSegToolSettingsViewModel(tool);

            Assert.Equal(tool.ModelPath, vm.ModelPath);
            Assert.Equal(tool.InputSize, vm.InputSize);
            Assert.Equal(tool.ConfidenceThreshold, vm.ConfidenceThreshold);
            Assert.Equal(tool.MaxInstances, vm.MaxInstances);
            Assert.Equal(tool.OverlayOpacity, vm.OverlayOpacity);
            Assert.Equal(tool.OutputMaskImage, vm.OutputMaskImage);

            // 화면에서 고친 값이 도구로 내려가야 한다
            vm.ConfidenceThreshold = 0.9f;
            Assert.Equal(0.9f, tool.ConfidenceThreshold);
        }

        /// <summary>모델이 입력 크기를 말해 주기 전에는 사람이 고칠 수 있어야 한다.</summary>
        [Fact]
        public void Input_size_is_editable_until_the_model_says_otherwise()
        {
            var vm = new RfdetrSegToolSettingsViewModel(new RfdetrSegTool());
            Assert.True(vm.InputSizeEditable);
        }

        /// <summary>
        /// 도구가 알리는 이름(InputSizeFromModel)과 화면이 보는 이름(InputSizeEditable)이 다르다.
        /// 이어 주지 않으면 모델을 열어도 칸이 잠기지 않는다 — 바꿔 봐야 되돌아가는데 고칠 수 있는 것처럼 보인다.
        /// </summary>
        [Fact]
        public void Editable_flag_is_announced_when_the_model_decides()
        {
            var tool = new RfdetrSegTool();
            var vm = new RfdetrSegToolSettingsViewModel(tool);

            var announced = new System.Collections.Generic.List<string?>();
            vm.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

            // 엔진이 모델 값을 알려 준 상황을 만든다 (설정자가 private 이라 리플렉션으로)
            typeof(RfdetrSegTool).GetProperty(nameof(RfdetrSegTool.InputSizeFromModel))!
                .SetValue(tool, true);

            Assert.Contains(nameof(RfdetrSegToolSettingsViewModel.InputSizeEditable), announced);
            Assert.False(vm.InputSizeEditable);
        }

        /// <summary>
        /// 모델 경로를 바꾸면 들고 있던 엔진을 버려야 한다. 안 버리면 새 모델을 골라도
        /// 옛 모델이 계속 추론하는데, 화면에는 새 경로가 보이므로 아무도 눈치채지 못한다.
        /// </summary>
        [Theory]
        [InlineData("RfdetrSegTool")]
        [InlineData("YoloSegTool")]
        [InlineData("SegmentationTool")]
        public void Changing_the_model_path_drops_the_cached_engine(string toolType)
        {
            var tool = VisionService.CreateTool(toolType);
            Assert.NotNull(tool);

            var field = tool!.GetType().GetField("_engine",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field);

            var setter = tool.GetType().GetProperty("ModelPath");
            Assert.NotNull(setter);
            setter!.SetValue(tool, @"D:\models\before.onnx");

            // 엔진을 들고 있는 상태를 만든다. 진짜 ONNX 없이 만들어야 하므로 생성자를 건너뛴다 —
            // 이 시험이 보는 것은 "경로가 바뀌면 버리는가" 하나뿐이라 엔진 속은 필요 없다.
            var engine = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(field!.FieldType);
            field.SetValue(tool, engine);
            Assert.NotNull(field.GetValue(tool));   // 준비가 실제로 됐는지 (안 되면 아래가 헛돈다)

            setter.SetValue(tool, @"D:\models\after.onnx");
            Assert.Null(field.GetValue(tool));
        }

        /// <summary>같은 경로를 다시 넣는 것은 바뀐 것이 아니다 — 그때까지 버리면 매번 다시 연다.</summary>
        [Fact]
        public void Setting_the_same_model_path_keeps_the_engine()
        {
            var tool = new RfdetrSegTool { ModelPath = @"D:\models\same.onnx" };
            var field = typeof(RfdetrSegTool).GetField("_engine",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            var engine = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(field.FieldType);
            field.SetValue(tool, engine);

            tool.ModelPath = @"D:\models\same.onnx";

            Assert.Same(engine, field.GetValue(tool));
        }

        /// <summary>도움말이 없으면 도움말 패널이 빈 채로 뜬다.</summary>
        [Fact]
        public void Has_help_content_for_every_parameter()
        {
            var help = VMS.VisionSetup.Models.HelpContent.GetToolHelp(ToolType);
            Assert.NotNull(help);
            Assert.False(string.IsNullOrWhiteSpace(help!.Name));
            Assert.False(string.IsNullOrWhiteSpace(help.Description));

            foreach (var parameter in new[]
                     {
                         "ModelPath", "InputSize", "ConfidenceThreshold", "MaxInstances",
                         "ShowOverlay", "OverlayOpacity", "DrawBoxes", "OutputMaskImage",
                     })
            {
                Assert.True(help.Parameters.ContainsKey(parameter), $"{parameter} 도움말이 없습니다");
            }
        }

        /// <summary>설정 화면 XAML 이 읽는 이름과 뷰모델이 내놓는 이름이 어긋나면 그 칸이 빈다.</summary>
        [Fact]
        public void Help_parameter_names_match_the_view_model()
        {
            var help = VMS.VisionSetup.Models.HelpContent.GetToolHelp(ToolType)!;
            var properties = typeof(RfdetrSegToolSettingsViewModel)
                .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .Select(p => p.Name)
                .ToHashSet();

            foreach (var parameter in help.Parameters.Keys)
                Assert.True(properties.Contains(parameter), $"뷰모델에 {parameter} 속성이 없습니다");
        }
    }
}
