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
                foreach (var tool in RecipeTemplateCatalog.CreateTools(template))
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
