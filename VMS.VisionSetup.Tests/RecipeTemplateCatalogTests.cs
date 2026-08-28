using System;
using System.IO;
using System.Linq;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 예제 템플릿 카탈로그 무결성 전수 검증:
    /// - 모든 ToolType 이 VisionService.CreateTool 로 생성 가능
    /// - 연결 인덱스 유효(범위 내, 자기 참조 없음)
    /// - 직렬화 왕복(SerializeTool→DeserializeTool) 시 ToolType 보존 (레시피 저장 호환)
    /// - 다이어그램 PNG 리소스 존재 (카탈로그 변경 후 재생성 누락 방지 —
    ///   재생성: dotnet test --filter TemplateDiagramGenerator)
    /// - 캔버스 배치 계산이 기존/신규 노드와 겹치지 않음
    /// </summary>
    public class RecipeTemplateCatalogTests
    {
        [Fact]
        public void AllTemplates_ToolsCreatable_And_ConnectionsValid()
        {
            Assert.NotEmpty(RecipeTemplateCatalog.Templates);

            foreach (var template in RecipeTemplateCatalog.Templates)
            {
                Assert.False(string.IsNullOrWhiteSpace(template.Id));
                Assert.False(string.IsNullOrWhiteSpace(template.Title));
                Assert.False(string.IsNullOrWhiteSpace(template.Category));

                if (template.Steps.Count > 0)
                {
                    // 다중 스텝 템플릿 — 스텝별로 검증 (단일 체인 Tools 는 비어 있어야 함)
                    Assert.Empty(template.Tools);
                    foreach (var stepSpec in template.Steps)
                    {
                        Assert.NotEmpty(stepSpec.Tools);
                        var stepTools = RecipeTemplateCatalog.CreateTools(stepSpec, template.Id);
                        Assert.Equal(stepSpec.Tools.Count, stepTools.Count);

                        foreach (var conn in stepSpec.Connections)
                        {
                            Assert.InRange(conn.SourceIndex, 0, stepTools.Count - 1);
                            Assert.InRange(conn.TargetIndex, 0, stepTools.Count - 1);
                            Assert.NotEqual(conn.SourceIndex, conn.TargetIndex);
                        }
                    }
                    continue;
                }

                Assert.NotEmpty(template.Tools);

                var tools = RecipeTemplateCatalog.CreateTools(template);
                Assert.Equal(template.Tools.Count, tools.Count);

                foreach (var conn in template.Connections)
                {
                    Assert.InRange(conn.SourceIndex, 0, tools.Count - 1);
                    Assert.InRange(conn.TargetIndex, 0, tools.Count - 1);
                    Assert.NotEqual(conn.SourceIndex, conn.TargetIndex);
                }
            }
        }

        [Fact]
        public void AllTemplates_Ids_AreUnique()
        {
            var ids = RecipeTemplateCatalog.Templates.Select(t => t.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void AllTemplates_Tools_SurviveSerializationRoundTrip()
        {
            foreach (var template in RecipeTemplateCatalog.Templates)
            {
                var allTools = template.Steps.Count > 0
                    ? template.Steps.SelectMany(s => RecipeTemplateCatalog.CreateTools(s, template.Id))
                    : RecipeTemplateCatalog.CreateTools(template).AsEnumerable();

                foreach (var tool in allTools)
                {
                    var config = ToolSerializer.SerializeTool(tool);
                    var restored = ToolSerializer.DeserializeTool(config);
                    Assert.NotNull(restored);
                    Assert.Equal(tool.ToolType, restored!.ToolType);
                    Assert.Equal(tool.Name, restored.Name);
                }
            }
        }

        [Fact]
        public void BuildSteps_MultiStepAlignTemplate_WiresCrossStepIds()
        {
            var template = RecipeTemplateCatalog.Templates.First(t => t.Id == "2d-multistep-align");

            var built = RecipeTemplateCatalog.BuildSteps(template, cameraId: "cam-1");

            Assert.Equal(2, built.Count);
            Assert.All(built, b => Assert.Equal("cam-1", b.Step.CameraId));

            // 스텝 B 의 MultiStepAlign 이 생성된 실제 스텝/툴 Id 로 배선되어 직렬화됨
            var alignConfig = built[1].Step.Tools.First(t => t.ToolType == "MultiStepAlignTool");
            var restored = Assert.IsType<VMS.VisionSetup.VisionTools.PatternMatching.MultiStepAlignTool>(
                ToolSerializer.DeserializeTool(alignConfig));

            Assert.Equal(built[0].Step.Id, restored.SourceStepIdA);
            Assert.Equal(built[0].Step.Tools.First(t => t.ToolType == "FeatureMatchTool").Id, restored.SourceToolIdA);
            Assert.Equal(built[1].Step.Id, restored.SourceStepIdB);
            Assert.Equal(built[1].Step.Tools.First(t => t.ToolType == "FeatureMatchTool").Id, restored.SourceToolIdB);

            // 스텝 내부 연결 직렬화 확인 (Grayscale → FM 은 Image, FM → Align 은 Result)
            var fmB = built[1].Step.Tools.First(t => t.ToolType == "FeatureMatchTool");
            Assert.Contains(alignConfig.Connections, c => c.SourceToolId == fmB.Id && c.ConnectionType == "Result");
        }

        [Fact]
        public void AlignCategory_Covers2DAnd3D()
        {
            var align = RecipeTemplateCatalog.Templates
                .Where(t => t.Category == RecipeTemplateCatalog.CategoryAlign)
                .ToList();

            // 2D 얼라인 3종 + 3D 얼라인 3종 — 갤러리 "얼라인" 탭이 두 축을 모두 제공
            Assert.Contains(align, t => t.Id == "2d-match-align");
            Assert.Contains(align, t => t.Id == "3d-registration-align");
            Assert.Contains(align, t => t.Id == "3d-plane-tilt-align");
            Assert.Contains(align, t => t.Id == "3d-hybrid-align");

            // 3D 얼라인은 전부 3D 카메라 배지를 달아야 한다 (2D 는 달지 않음)
            foreach (var t in align.Where(t => t.Id.StartsWith("3d-")))
                Assert.Contains("3D 카메라 필요", t.Prerequisites);
            foreach (var t in align.Where(t => t.Id.StartsWith("2d-")))
                Assert.DoesNotContain("3D 카메라 필요", t.Prerequisites);
        }

        [Fact]
        public void AlignTemplates_ParameterPresets_SurviveRoundTrip()
        {
            // 프리셋이 직렬화에서 유실되면 갤러리로 만든 체인이 기본값으로 되돌아간다
            // (레시피 저장/로드 후 조용히 동작이 달라지는 사고 방지).
            var reg = RoundTripFirst<VMS.VisionSetup.VisionTools.PointCloud.PointCloudRegistrationTool>(
                "3d-registration-align");
            Assert.False(reg.ApplyTransformToSource);   // 얼라인 = 변위 측정, 점군 덮어쓰기 안 함

            var geo = RoundTripFirst<VMS.VisionSetup.VisionTools.Measurement.Geometry3DTool>(
                "3d-plane-tilt-align");
            Assert.Equal(VMS.VisionSetup.VisionTools.Measurement.Geometry3DOperation.PlaneToPlaneAngle,
                geo.Operation);
            Assert.False(geo.UseManualPoints);          // 연결된 Plane Fit 2개를 소스로 사용

            var match = RoundTripFirst<VMS.VisionSetup.VisionTools.PatternMatching.MatchAlignTool>(
                "2d-match-align-2pt");
            Assert.Equal(VMS.VisionSetup.VisionTools.PatternMatching.MatchAlignMode.TwoPoint, match.Mode);
        }

        /// <summary>템플릿의 첫 번째 T 툴을 직렬화 왕복시켜 반환.</summary>
        private static T RoundTripFirst<T>(string templateId) where T : VisionToolBase
        {
            var template = RecipeTemplateCatalog.Templates.First(t => t.Id == templateId);
            var tool = RecipeTemplateCatalog.CreateTools(template).OfType<T>().First();
            var restored = ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(tool));
            return Assert.IsType<T>(restored);
        }

        [Fact]
        public void AllTemplates_DiagramPng_Exists()
        {
            var resourceDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\..\VMS.VisionSetup\Resources\Templates"));

            foreach (var template in RecipeTemplateCatalog.Templates)
            {
                var png = Path.Combine(resourceDir, $"{template.Id}.png");
                Assert.True(File.Exists(png),
                    $"다이어그램 누락: {png} — dotnet test --filter TemplateDiagramGenerator 로 재생성");
            }
        }

        [Fact]
        public void ComputeInsertPositions_DoesNotOverlap_ExistingOrEachOther()
        {
            // 기존 노드 2행이 있는 워크스페이스에 3개 툴 체인 삽입
            var existing = new (double X, double Y)[] { (30, 30), (220, 30), (30, 140) };
            var positions = RecipeTemplateCatalog.ComputeInsertPositions(existing, 3);

            Assert.Equal(3, positions.Count);

            // 새 행은 기존 최하단(140)보다 아래
            Assert.All(positions, p => Assert.True(p.Y > 140));

            // 신규 노드끼리 X 간격 = NodeSpacingX (겹침 없음)
            for (int i = 1; i < positions.Count; i++)
                Assert.Equal(RecipeTemplateCatalog.NodeSpacingX,
                    positions[i].X - positions[i - 1].X);

            // 빈 워크스페이스면 시작 위치부터
            var fresh = RecipeTemplateCatalog.ComputeInsertPositions(
                Array.Empty<(double, double)>(), 2);
            Assert.Equal(RecipeTemplateCatalog.StartY, fresh[0].Y);
            Assert.Equal(RecipeTemplateCatalog.StartX, fresh[0].X);
        }
    }
}
