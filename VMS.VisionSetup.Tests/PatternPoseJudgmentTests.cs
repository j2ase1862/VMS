using System.Collections.Generic;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// FeatureMatch/ShapeMatch 각도·스케일 판정 + Web 파라미터 연동 —
    /// Web Pattern 프리셋(Angle/Scale Lower·Upper Limit)과 1:1 대응.
    /// </summary>
    public class PatternPoseJudgmentTests
    {
        // ── 판정 로직 ──

        [Theory]
        [InlineData(0.0, 1.0, true)]     // 범위 안 → 통과
        [InlineData(-10.1, 1.0, false)]  // 각도 하한 미달
        [InlineData(10.1, 1.0, false)]   // 각도 상한 초과
        [InlineData(0.0, 0.89, false)]   // 스케일 하한 미달
        [InlineData(0.0, 1.11, false)]   // 스케일 상한 초과
        public void FeatureMatch_EvaluatePoseJudgment_Bounds(double angle, double scale, bool pass)
        {
            var tool = new FeatureMatchTool
            {
                UseAngleJudgment = true,
                AngleLowerLimit = -10,
                AngleUpperLimit = 10,
                UseScaleJudgment = true,
                ScaleLowerLimit = 0.9,
                ScaleUpperLimit = 1.1,
            };

            var ng = tool.EvaluatePoseJudgment(angle, scale);
            Assert.Equal(pass, ng == null);
        }

        [Fact]
        public void EvaluatePoseJudgment_Disabled_AlwaysPasses()
        {
            // 기본값(비활성)에서는 어떤 자세든 통과 — 기존 레시피 동작 보존.
            var feature = new FeatureMatchTool();
            var shape = new ShapeMatchTool();

            Assert.Null(feature.EvaluatePoseJudgment(179.9, 3.0));
            Assert.Null(shape.EvaluatePoseJudgment(-179.9, 0.01));
        }

        [Theory]
        [InlineData(5.0, 1.0, true)]
        [InlineData(45.1, 1.0, false)]
        [InlineData(5.0, 1.21, false)]
        public void ShapeMatch_EvaluatePoseJudgment_Bounds(double angle, double scale, bool pass)
        {
            var tool = new ShapeMatchTool
            {
                UseAngleJudgment = true,
                AngleLowerLimit = -45,
                AngleUpperLimit = 45,
                UseScaleJudgment = true,
                ScaleLowerLimit = 0.8,
                ScaleUpperLimit = 1.2,
            };

            Assert.Equal(pass, tool.EvaluatePoseJudgment(angle, scale) == null);
        }

        // ── VM 연동 왕복 ──

        [Fact]
        public void FeatureMatch_VmLinkSetters_UpdateToolLinkedParamCodes()
        {
            var tool = new FeatureMatchTool();
            var vm = new FeatureMatchToolSettingsViewModel(tool);

            vm.SelectedAngleLowerLimitCode = new ParamCodeItem { ParamCode = 21 };
            vm.SelectedAngleUpperLimitCode = new ParamCodeItem { ParamCode = 22 };
            vm.SelectedScaleLowerLimitCode = new ParamCodeItem { ParamCode = 23 };
            vm.SelectedScaleUpperLimitCode = new ParamCodeItem { ParamCode = 24 };

            Assert.Equal(21, tool.LinkedParamCodes["AngleLowerLimit"]);
            Assert.Equal(22, tool.LinkedParamCodes["AngleUpperLimit"]);
            Assert.Equal(23, tool.LinkedParamCodes["ScaleLowerLimit"]);
            Assert.Equal(24, tool.LinkedParamCodes["ScaleUpperLimit"]);

            // 연동 해제 (None 선택) / null push 는 no-op (#299 회귀 방지)
            vm.SelectedAngleLowerLimitCode = new ParamCodeItem { ParamCode = null };
            Assert.False(tool.LinkedParamCodes.ContainsKey("AngleLowerLimit"));
            vm.SelectedAngleUpperLimitCode = null;
            Assert.Equal(22, tool.LinkedParamCodes["AngleUpperLimit"]);
        }

        [Fact]
        public void ShapeMatch_VmLinkSetters_UpdateToolLinkedParamCodes()
        {
            var tool = new ShapeMatchTool();
            var vm = new ShapeMatchToolSettingsViewModel(tool);

            vm.SelectedAngleLowerLimitCode = new ParamCodeItem { ParamCode = 31 };
            vm.SelectedScaleUpperLimitCode = new ParamCodeItem { ParamCode = 34 };

            Assert.Equal(31, tool.LinkedParamCodes["AngleLowerLimit"]);
            Assert.Equal(34, tool.LinkedParamCodes["ScaleUpperLimit"]);

            vm.SelectedAngleLowerLimitCode = null; // null push no-op
            Assert.Equal(31, tool.LinkedParamCodes["AngleLowerLimit"]);
        }

        // ── 직렬화 왕복 ──

        [Fact]
        public void FeatureMatch_Serializer_RoundTrips_JudgmentParamsAndLinks()
        {
            var original = new FeatureMatchTool
            {
                UseAngleJudgment = true,
                AngleLowerLimit = -5.5,
                AngleUpperLimit = 7.25,
                UseScaleJudgment = true,
                ScaleLowerLimit = 0.85,
                ScaleUpperLimit = 1.15,
                LinkedParamCodes = new Dictionary<string, int>
                {
                    ["AngleLowerLimit"] = 21,
                    ["ScaleUpperLimit"] = 24,
                },
            };

            var config = ToolSerializer.SerializeTool(original);
            var restored = Assert.IsType<FeatureMatchTool>(ToolSerializer.DeserializeTool(config));

            Assert.True(restored.UseAngleJudgment);
            Assert.Equal(-5.5, restored.AngleLowerLimit);
            Assert.Equal(7.25, restored.AngleUpperLimit);
            Assert.True(restored.UseScaleJudgment);
            Assert.Equal(0.85, restored.ScaleLowerLimit);
            Assert.Equal(1.15, restored.ScaleUpperLimit);
            Assert.Equal(21, restored.LinkedParamCodes["AngleLowerLimit"]);
            Assert.Equal(24, restored.LinkedParamCodes["ScaleUpperLimit"]);
        }

        [Fact]
        public void ShapeMatch_Serializer_RoundTrips_JudgmentParamsAndLinks()
        {
            var original = new ShapeMatchTool
            {
                UseAngleJudgment = true,
                AngleLowerLimit = -30,
                AngleUpperLimit = 30,
                UseScaleJudgment = true,
                ScaleLowerLimit = 0.7,
                ScaleUpperLimit = 1.3,
                LinkedParamCodes = new Dictionary<string, int>
                {
                    ["AngleUpperLimit"] = 32,
                    ["ScaleLowerLimit"] = 33,
                },
            };

            var config = ToolSerializer.SerializeTool(original);
            var restored = Assert.IsType<ShapeMatchTool>(ToolSerializer.DeserializeTool(config));

            Assert.True(restored.UseAngleJudgment);
            Assert.Equal(-30, restored.AngleLowerLimit);
            Assert.Equal(30, restored.AngleUpperLimit);
            Assert.True(restored.UseScaleJudgment);
            Assert.Equal(0.7, restored.ScaleLowerLimit);
            Assert.Equal(1.3, restored.ScaleUpperLimit);
            Assert.Equal(32, restored.LinkedParamCodes["AngleUpperLimit"]);
            Assert.Equal(33, restored.LinkedParamCodes["ScaleLowerLimit"]);
        }

        [Fact]
        public void Clone_CopiesJudgmentParams()
        {
            var feature = new FeatureMatchTool
            {
                UseAngleJudgment = true, AngleLowerLimit = -1, AngleUpperLimit = 2,
                UseScaleJudgment = true, ScaleLowerLimit = 0.95, ScaleUpperLimit = 1.05,
            };
            var featureClone = Assert.IsType<FeatureMatchTool>(feature.Clone());
            Assert.True(featureClone.UseAngleJudgment);
            Assert.Equal(-1, featureClone.AngleLowerLimit);
            Assert.Equal(1.05, featureClone.ScaleUpperLimit);

            var shape = new ShapeMatchTool
            {
                UseScaleJudgment = true, ScaleLowerLimit = 0.6, ScaleUpperLimit = 1.4,
            };
            var shapeClone = Assert.IsType<ShapeMatchTool>(shape.Clone());
            Assert.True(shapeClone.UseScaleJudgment);
            Assert.Equal(0.6, shapeClone.ScaleLowerLimit);
            Assert.Equal(1.4, shapeClone.ScaleUpperLimit);
        }
    }
}
