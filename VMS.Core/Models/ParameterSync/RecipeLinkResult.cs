namespace VMS.Core.Models.ParameterSync
{
    /// <summary>운전 레시피를 이름으로 Web 레시피에 연결한 결과.</summary>
    public enum RecipeLinkOutcome
    {
        /// <summary>이미 그 Web 레시피에 연결돼 있다 — 아무것도 바꾸지 않았다.</summary>
        AlreadyLinked,

        /// <summary>Web 레시피로 바꿔 연결하고 파라미터를 받았다.</summary>
        Linked,

        /// <summary>레시피 목록을 받아 봤는데 그 이름이 없다 — Web 에 등록되지 않은 레시피.</summary>
        NotOnWeb,

        /// <summary>레시피 목록을 받지 못했다 — Web 이 끊겼다. 주기 동기화가 이 이름으로 다시 찾는다.</summary>
        WebUnavailable,

        /// <summary>이름은 찾았지만 그 레시피의 파라미터를 받지 못했다. 주기 동기화가 다시 시도한다.</summary>
        LoadFailed,

        /// <summary>운전 레시피가 없다.</summary>
        NoRecipe,

        /// <summary>끝나기 전에 다른 호출이 레시피를 바꿨다 — 이 호출은 연결을 바꾸지 않았다.</summary>
        Superseded,
    }

    /// <summary>
    /// <see cref="RecipeLinkOutcome"/> 과 연결된 Web 레시피 ID(연결되지 않았으면 0),
    /// 직전에 연결돼 있던 Web 레시피 ID(없었으면 0).
    /// </summary>
    public readonly record struct RecipeLinkResult(RecipeLinkOutcome Outcome, int RecipeId, int PreviousRecipeId)
    {
        /// <summary>지금 Web 레시피에 연결돼 있는가.</summary>
        public bool IsLinked => Outcome is RecipeLinkOutcome.AlreadyLinked or RecipeLinkOutcome.Linked;

        /// <summary>다른 Web 레시피에 연결돼 있다가 이번에 끊겼는가.</summary>
        public bool DroppedPrevious => PreviousRecipeId > 0 && Outcome is RecipeLinkOutcome.NotOnWeb
            or RecipeLinkOutcome.WebUnavailable or RecipeLinkOutcome.LoadFailed or RecipeLinkOutcome.NoRecipe;
    }
}
