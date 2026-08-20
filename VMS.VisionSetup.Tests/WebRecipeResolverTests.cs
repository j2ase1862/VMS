using System.Collections.Generic;
using VMS.Core.Models.ParameterSync;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 시작 시 파라미터 캐시 초기 레시피 결정 — VMS 에서 레시피를 넘겨받아 실행된
    /// 경우 첫 레시피(A)가 아니라 현재 레시피(B)를 로드해야 한다 (2026-08-20 재현).
    /// </summary>
    public class WebRecipeResolverTests
    {
        private static List<RecipeSummaryDto> Recipes() => new()
        {
            new RecipeSummaryDto { Id = 1, Name = "A" },
            new RecipeSummaryDto { Id = 2, Name = "B" },
        };

        [Fact]
        public void CurrentWebRecipeId_TakesPriority()
        {
            Assert.Equal(2, WebRecipeResolver.Resolve("무관한이름", 2, Recipes()));
        }

        [Fact]
        public void NameMatch_WhenNoWebRecipeId()
        {
            // WebRecipeId 미기록 파일이라도 이름으로 B 를 찾아야 함 (대소문자 무시)
            Assert.Equal(2, WebRecipeResolver.Resolve("b", null, Recipes()));
        }

        [Fact]
        public void FirstRecipe_WhenNoCurrentRecipe()
        {
            // 단독 실행(현재 레시피 없음) — 기존 동작 유지: 첫 레시피
            Assert.Equal(1, WebRecipeResolver.Resolve(null, null, Recipes()));
        }

        [Fact]
        public void FirstRecipe_WhenNameNotFound()
        {
            Assert.Equal(1, WebRecipeResolver.Resolve("없는이름", null, Recipes()));
        }

        [Fact]
        public void Null_WhenNoRecipes()
        {
            Assert.Null(WebRecipeResolver.Resolve("B", null, new List<RecipeSummaryDto>()));
        }

        [Fact]
        public void InvalidWebRecipeId_FallsThrough()
        {
            // 0/음수 ID 는 무효 — 이름 매칭으로 폴백
            Assert.Equal(2, WebRecipeResolver.Resolve("B", 0, Recipes()));
            Assert.Equal(2, WebRecipeResolver.Resolve("B", -1, Recipes()));
        }
    }
}
