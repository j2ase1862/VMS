using CommunityToolkit.Mvvm.ComponentModel;
using VMS.Core.Security;

namespace VMS.ViewModels
{
    /// <summary>
    /// RetentionSettingsViewModel.CategoryRetentions 의 한 행 — 단일 AuditCategory 의 보존 일수.
    /// </summary>
    public partial class CategoryRetentionItem : ObservableObject
    {
        public AuditCategory Category { get; init; }

        /// <summary>UI 표시용 카테고리 이름 (enum 그대로).</summary>
        public string Name => Category.ToString();

        /// <summary>UI 도움말 — 이 카테고리가 무엇을 추적하는지.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>Clamp 범위 라벨.</summary>
        public string Range => "[1, 3650]";

        [ObservableProperty] private int _days;
    }
}
