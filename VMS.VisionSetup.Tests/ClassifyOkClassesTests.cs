using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// ClassifyTool 양품 클래스(OK Classes) 판정.
    ///
    /// 종전 판정은 "Top-1 신뢰도 ≥ 임계값" 뿐이라, 불량 클래스로 자신 있게 분류될수록 OK 가 됐다
    /// (2026-10-16 GS 딥러닝 시험자료 — 결함 ①). 스텁은 입력 채널 평균을 logits[1,3] 로 내므로
    /// 한 채널만 밝은 영상은 그 채널의 클래스로 높은 신뢰도, 회색 영상은 낮은 신뢰도가 나온다.
    /// </summary>
    public class ClassifyOkClassesTests : IDisposable
    {
        private const string Names = "good, pen, tape";
        private readonly string _model;

        public ClassifyOkClassesTests()
        {
            _model = Path.Combine(Path.GetTempPath(), $"vms-classify-ok-{Guid.NewGuid():N}.onnx");
            File.WriteAllBytes(_model, Convert.FromBase64String(AnomalyModelContractTests.ClassifierStub));
        }

        public void Dispose()
        {
            try { File.Delete(_model); } catch { }
        }

        private ClassifyTool NewTool(string okClasses = "", double threshold = 0.5) => new()
        {
            ModelPath = _model,
            InputWidth = 16,
            InputHeight = 16,
            ClassNamesText = Names,
            OkClassesText = okClasses,
            ConfidenceThreshold = threshold,
            DrawOverlay = false,
        };

        private static Mat Bright() => new(32, 32, MatType.CV_8UC3, new Scalar(0, 0, 255));   // 한 채널만 밝음 → 높은 신뢰도
        private static Mat Gray() => new(32, 32, MatType.CV_8UC3, new Scalar(128, 128, 128)); // 세 채널 비슷 → 낮은 신뢰도

        /// <summary>밝은 영상이 분류되는 클래스와 신뢰도 (스텁의 채널 순서에 기대지 않으려고 실제로 돌려 본다)</summary>
        private (string Top, double Confidence) Probe(Mat img)
        {
            var r = NewTool().Execute(img);
            Assert.True(r.Data.ContainsKey("ClassName"), r.Message);
            return ((string)r.Data["ClassName"], Convert.ToDouble(r.Data["Confidence"]));
        }

        [Fact]
        public void NoOkClasses_KeepsLegacyConfidenceOnlyJudgment()
        {
            using var img = Bright();
            var (top, conf) = Probe(img);
            Assert.True(conf > 0.9);

            var r = NewTool().Execute(img);
            Assert.True(r.Success);   // 어느 클래스든 신뢰도만 넘으면 OK — 기존 레시피 동작 그대로
            Assert.Equal($"{top} ({conf:P1})", r.Message);
        }

        [Fact]
        public void TopIsOkClass_AndConfident_IsOk()
        {
            using var img = Bright();
            var (top, _) = Probe(img);

            var r = NewTool(okClasses: top).Execute(img);
            Assert.True(r.Success, r.Message);
        }

        [Fact]
        public void TopIsDefectClass_EvenWhenConfident_IsNg()
        {
            using var img = Bright();
            var (top, _) = Probe(img);
            var others = string.Join(", ", Names.Split(", ").Where(n => n != top));

            var r = NewTool(okClasses: others).Execute(img);
            Assert.False(r.Success);
            Assert.Contains("불량 클래스", r.Message);
            Assert.Equal(top, r.Data["ClassName"]);
        }

        [Fact]
        public void OkClass_ButNotConfident_IsNg()
        {
            using var img = Gray();
            var (top, conf) = Probe(img);
            Assert.True(conf < 0.9);

            var r = NewTool(okClasses: top, threshold: 0.9).Execute(img);
            Assert.False(r.Success);
            Assert.Contains("신뢰도 부족", r.Message);
        }

        [Fact]
        public void OkClassMatch_IsCaseInsensitive_AndAllowsSeveral()
        {
            using var img = Bright();
            var (top, _) = Probe(img);

            var r = NewTool(okClasses: $"nothing-else-here, {top.ToUpperInvariant()}").Execute(img);
            // "nothing-else-here" 는 Class Names 에 없으므로 실행 전에 막혀야 한다
            Assert.False(r.Success);
            Assert.Contains("Class Names 에 없습니다", r.Message);

            var others = Names.Split(", ").First(n => n != top);
            var ok = NewTool(okClasses: $"{others}, {top.ToUpperInvariant()}").Execute(img);
            Assert.True(ok.Success, ok.Message);
        }

        [Fact]
        public void UnknownOkClass_FailsBeforeInference()
        {
            using var img = Bright();
            var r = NewTool(okClasses: "goood").Execute(img);
            Assert.False(r.Success);
            Assert.Contains("'goood'", r.Message);
            Assert.False(r.Data.ContainsKey("ClassName"));
        }

        [Fact]
        public void Serializer_RoundTrips_OkClasses_AndLegacyRecipeHasNone()
        {
            var original = NewTool(okClasses: "good");
            var json = JsonSerializer.Serialize(ToolSerializer.SerializeTool(original));
            var restored = Assert.IsType<ClassifyTool>(ToolSerializer.DeserializeTool(JsonSerializer.Deserialize<ToolConfig>(json)!));
            Assert.Equal("good", restored.OkClassesText);

            var legacy = ToolSerializer.SerializeTool(NewTool());
            legacy.Parameters.Remove("OkClassesText");
            var fromLegacy = Assert.IsType<ClassifyTool>(ToolSerializer.DeserializeTool(legacy));
            Assert.Empty(fromLegacy.OkClasses);   // 옛 레시피 = 지정 없음 = 종전 판정
        }

        [Fact]
        public void Clone_CopiesOkClasses()
        {
            var clone = Assert.IsType<ClassifyTool>(NewTool(okClasses: "good, pen").Clone());
            Assert.Equal("good, pen", clone.OkClassesText);
        }

        // ── Settings VM 왕복 (VM 래퍼 누락 시 화면 공백+저장 불가 회귀 방지) ──

        [Fact]
        public void SettingsViewModel_CheckboxesWriteOkClasses_InClassNameOrder()
        {
            var tool = NewTool();
            using var vm = new ClassifyToolSettingsViewModel(tool);

            Assert.Equal(new[] { "good", "pen", "tape" }, vm.OkClassOptions.Select(o => o.Name));
            Assert.True(vm.HasNoOkClasses);

            vm.OkClassOptions[2].IsOk = true;   // tape
            vm.OkClassOptions[0].IsOk = true;   // good
            Assert.Equal("good, tape", tool.OkClassesText);
            Assert.False(vm.HasNoOkClasses);
        }

        [Fact]
        public void SettingsViewModel_ReflectsToolChanges()
        {
            var tool = NewTool(okClasses: "pen");
            using var vm = new ClassifyToolSettingsViewModel(tool);
            Assert.True(vm.OkClassOptions.Single(o => o.Name == "pen").IsOk);

            tool.ClassNamesText = "ok, ng";   // 모델 교체로 클래스가 바뀐 경우
            Assert.Equal(new[] { "ok", "ng" }, vm.OkClassOptions.Select(o => o.Name));
            Assert.All(vm.OkClassOptions, o => Assert.False(o.IsOk));

            tool.OkClassesText = "ok";
            Assert.True(vm.OkClassOptions.Single(o => o.Name == "ok").IsOk);
        }

        [Fact]
        public void HelpContent_HasOkClassesEntry()
        {
            Assert.False(string.IsNullOrWhiteSpace(HelpContent.GetParameterHelp("ClassifyTool", "OkClassesText")));
        }
    }
}
