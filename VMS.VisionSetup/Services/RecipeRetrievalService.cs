using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 키워드 기반 단순 매칭으로 유사 레시피 검색. 단계 2 RAG의 v1 구현.
    /// 벡터 임베딩 인프라 없이 토큰 절약형으로 동작.
    /// </summary>
    public class RecipeRetrievalService : IRecipeRetrievalService
    {
        private readonly IRecipeService _recipeService;

        public RecipeRetrievalService(IRecipeService recipeService)
        {
            _recipeService = recipeService;
        }

        public string? BuildSimilarRecipesContext(string userMessage, int topK = 2)
        {
            if (string.IsNullOrWhiteSpace(userMessage)) return null;

            List<RecipeInfo> all;
            try
            {
                all = _recipeService.GetRecipeList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RecipeRetrieval] GetRecipeList failed: {ex.Message}");
                return null;
            }

            if (all == null || all.Count == 0) return null;

            // 사용자 메시지에서 의미 있는 토큰 추출 (공백 분리, 2글자 이상)
            var queryTokens = Tokenize(userMessage);
            if (queryTokens.Count == 0) return null;

            // 점수 매기기: Name + ModifiedAtDisplay/Author 같은 메타는 제외, Name만 기준
            var scored = all
                .Select(info => new { Info = info, Score = ScoreName(info.Name, queryTokens) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Info.ModifiedAt)
                .Take(topK)
                .ToList();

            if (scored.Count == 0) return null;

            // 풀 레시피 로드 → 도구 시퀀스 + Description 추출
            var blocks = new List<string>();
            foreach (var s in scored)
            {
                Recipe? recipe = null;
                try { recipe = _recipeService.LoadRecipe(s.Info.FilePath); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[RecipeRetrieval] LoadRecipe failed for '{s.Info.Name}': {ex.Message}");
                    continue;
                }
                if (recipe == null) continue;

                blocks.Add(FormatRecipe(recipe));
            }

            if (blocks.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("[Similar Recipes]");
            sb.AppendLine("아래는 사용자의 현재 요청과 키워드가 비슷한 과거 레시피입니다.");
            sb.AppendLine("구조 참고용이며 그대로 복사하지 말고, 사용자의 현재 요청에 맞게 조정하십시오.");
            sb.AppendLine();
            sb.AppendLine(string.Join("\n---\n", blocks));
            return sb.ToString();
        }

        // ─────────────── 내부 헬퍼 ───────────────

        private static List<string> Tokenize(string text)
        {
            return text.Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '\t', '\n', '\r' },
                              StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length >= 2)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Name에 query token이 포함되어 있는지 카운트. 단순 substring contain.
        /// </summary>
        private static int ScoreName(string name, IEnumerable<string> queryTokens)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string lower = name.ToLowerInvariant();
            int score = 0;
            foreach (var t in queryTokens)
            {
                if (lower.Contains(t)) score++;
            }
            return score;
        }

        /// <summary>
        /// Recipe → LLM 친화 압축 표현 (Name, Description, 도구 시퀀스만).
        /// </summary>
        private static string FormatRecipe(Recipe recipe)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Recipe: {recipe.Name}");

            if (!string.IsNullOrWhiteSpace(recipe.Description))
                sb.AppendLine($"Description: {recipe.Description.Trim()}");

            if (recipe.Tags != null && recipe.Tags.Count > 0)
                sb.AppendLine($"Tags: {string.Join(", ", recipe.Tags)}");

            // 모든 step의 도구를 순서대로 평탄화
            var toolTypes = recipe.Steps?
                .SelectMany(s => s.Tools ?? new List<ToolConfig>())
                .Select(t => t.ToolType)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList() ?? new List<string>();

            if (toolTypes.Count > 0)
                sb.AppendLine($"Sequence: {string.Join(" → ", toolTypes)}");

            return sb.ToString().TrimEnd();
        }
    }
}
