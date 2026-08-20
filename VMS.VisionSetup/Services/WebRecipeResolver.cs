using System;
using System.Collections.Generic;
using System.Linq;
using VMS.Core.Models.ParameterSync;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 시작 시 Web 파라미터 캐시로 로드할 레시피 결정.
    /// 우선순위: ① 현재 레시피의 WebRecipeId ② 현재 레시피와 이름 일치 ③ 첫 번째 레시피.
    /// 종전에는 무조건 ③이라, VMS 에서 레시피를 넘겨받아 실행된 경우(현재 레시피 = B)
    /// 콤보가 첫 레시피(A)로 뒤집히는 시작 경합이 있었다 (2026-08-20 실증 재현).
    /// </summary>
    internal static class WebRecipeResolver
    {
        public static int? Resolve(
            string? currentRecipeName,
            int? currentWebRecipeId,
            IReadOnlyList<RecipeSummaryDto> recipes)
        {
            if (currentWebRecipeId is int id && id > 0)
                return id;

            if (!string.IsNullOrWhiteSpace(currentRecipeName))
            {
                var byName = recipes.FirstOrDefault(r =>
                    string.Equals(r.Name, currentRecipeName, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                    return byName.Id;
            }

            return recipes.Count > 0 ? recipes[0].Id : null;
        }
    }
}
