using System;
using System.Collections.Generic;
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
    /// DetectionTool NG 클래스(NG Classes) 판정.
    ///
    /// 종전 판정은 "하나 이상 검출되면 OK" 뿐이라, 결함 클래스를 학습한 모델로 결함을 찾아도 OK 가 됐다
    /// (2026-11-16 사내 매뉴얼 검토 — D-25 ⑤). 검출 엔진 없이 판정 규칙(Judge)과 설정 왕복을 본다.
    /// </summary>
    public class DetectionNgClassesTests
    {
        private static readonly string[] Names = { "part", "scratch", "crack" };

        private static DetectionResult Det(int classId) => new() { ClassId = classId, Confidence = 0.9f, Width = 10, Height = 10 };

        private static DetectionTool NewTool(string ngClasses = "") => new()
        {
            ClassNamesText = string.Join(", ", Names),
            NgClassesText = ngClasses,
            DrawOverlay = false,
        };

        [Fact]
        public void NoNgClasses_KeepsLegacyAnyDetectionIsOk()
        {
            var none = Array.Empty<string>();
            Assert.True(DetectionTool.Judge(new List<DetectionResult> { Det(1) }, Names, none, false, true).Success);
            Assert.False(DetectionTool.Judge(new List<DetectionResult>(), Names, none, false, true).Success);
        }

        [Fact]
        public void NgClasses_AnyNgDetection_IsNg_AndCounted()
        {
            var ng = new[] { "scratch", "CRACK" };   // 대소문자 무시
            var (success, ngCount) = DetectionTool.Judge(
                new List<DetectionResult> { Det(0), Det(1), Det(2), Det(2) }, Names, ng, false, true);
            Assert.False(success);
            Assert.Equal(3, ngCount);
        }

        [Fact]
        public void NgClasses_OnlyOkClassesOrNothing_IsOk()
        {
            var ng = new[] { "scratch" };
            Assert.True(DetectionTool.Judge(new List<DetectionResult> { Det(0), Det(2) }, Names, ng, false, true).Success);
            Assert.True(DetectionTool.Judge(new List<DetectionResult>(), Names, ng, false, true).Success);
        }

        [Fact]
        public void NgClasses_WithDotAnalysis_ClusterFailureStillNg()
        {
            var ng = new[] { "scratch" };
            Assert.False(DetectionTool.Judge(new List<DetectionResult> { Det(0) }, Names, ng, true, false).Success);
            Assert.True(DetectionTool.Judge(new List<DetectionResult> { Det(0) }, Names, ng, true, true).Success);
        }

        [Fact]
        public void ClassIdOutsideClassNames_IsNotNg()
        {
            Assert.False(DetectionTool.IsNgDetection(Det(7), Names, new[] { "scratch" }));
        }

        [Fact]
        public void UnknownNgClass_FailsBeforeInference()
        {
            var tool = NewTool(ngClasses: "dent");
            tool.ModelPath = Path.Combine(Path.GetTempPath(), $"vms-det-ng-missing-{Guid.NewGuid():N}.onnx");
            tool.ClassNamesText = string.Join(", ", Names);   // 없는 모델 경로라 메타데이터로 덮이지 않게 다시 넣는다
            using var img = new Mat(32, 32, MatType.CV_8UC3, Scalar.Black);

            var r = tool.Execute(img);

            Assert.False(r.Success);
            Assert.Contains("dent", r.Message);
            Assert.Contains("NG Classes", r.Message);
        }

        [Fact]
        public void Serializer_RoundTrips_NgClasses_AndLegacyRecipeHasNone()
        {
            var json = JsonSerializer.Serialize(ToolSerializer.SerializeTool(NewTool(ngClasses: "scratch, crack")));
            var restored = Assert.IsType<DetectionTool>(ToolSerializer.DeserializeTool(JsonSerializer.Deserialize<ToolConfig>(json)!));
            Assert.Equal("scratch, crack", restored.NgClassesText);

            var legacy = ToolSerializer.SerializeTool(NewTool());
            legacy.Parameters.Remove("NgClassesText");
            var fromLegacy = Assert.IsType<DetectionTool>(ToolSerializer.DeserializeTool(legacy));
            Assert.Empty(fromLegacy.NgClasses);   // 옛 레시피 = 지정 없음 = 종전 판정
        }

        [Fact]
        public void Clone_CopiesNgClasses()
        {
            var clone = Assert.IsType<DetectionTool>(NewTool(ngClasses: "crack").Clone());
            Assert.Equal("crack", clone.NgClassesText);
        }

        [Fact]
        public void ResultKeys_IncludeNgCount()
        {
            Assert.Contains("NgCount", NewTool().GetAvailableResultKeys());
        }

        // ── Settings VM 왕복 (VM 래퍼 누락 시 화면 공백+저장 불가 회귀 방지) ──

        [Fact]
        public void SettingsViewModel_CheckboxesWriteNgClasses_InClassNameOrder()
        {
            var tool = NewTool();
            using var vm = new DetectionToolSettingsViewModel(tool);

            Assert.Equal(Names, vm.NgClassOptions.Select(o => o.Name));
            Assert.True(vm.HasNoNgClasses);

            vm.NgClassOptions[2].IsNg = true;   // crack
            vm.NgClassOptions[1].IsNg = true;   // scratch
            Assert.Equal("scratch, crack", tool.NgClassesText);
            Assert.False(vm.HasNoNgClasses);
        }

        [Fact]
        public void SettingsViewModel_ReflectsToolChanges()
        {
            var tool = NewTool(ngClasses: "scratch");
            using var vm = new DetectionToolSettingsViewModel(tool);
            Assert.True(vm.NgClassOptions.Single(o => o.Name == "scratch").IsNg);

            tool.ClassNamesText = "ok, ng";   // 모델 교체로 클래스가 바뀐 경우
            Assert.Equal(new[] { "ok", "ng" }, vm.NgClassOptions.Select(o => o.Name));
            Assert.All(vm.NgClassOptions, o => Assert.False(o.IsNg));

            tool.NgClassesText = "ng";
            Assert.True(vm.NgClassOptions.Single(o => o.Name == "ng").IsNg);
        }

        [Fact]
        public void HelpContent_HasNgClassesEntry()
        {
            Assert.False(string.IsNullOrWhiteSpace(HelpContent.GetParameterHelp("DetectionTool", "NgClassesText")));
        }
    }
}
