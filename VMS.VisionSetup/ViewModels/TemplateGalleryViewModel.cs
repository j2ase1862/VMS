using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>
    /// 예제 템플릿 갤러리 다이얼로그 ViewModel.
    /// 카탈로그를 카테고리별로 묶어 표시하고, 선택된 템플릿을 다이얼로그 결과로 넘긴다.
    /// </summary>
    public partial class TemplateGalleryViewModel : ObservableObject
    {
        /// <summary>카테고리 탭 하나 (이름 + 소속 템플릿).</summary>
        public class TemplateCategoryGroup
        {
            public string Name { get; init; } = string.Empty;
            public IReadOnlyList<RecipeTemplate> Templates { get; init; } = Array.Empty<RecipeTemplate>();
        }

        public IReadOnlyList<TemplateCategoryGroup> Categories { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        private RecipeTemplate? _selectedTemplate;

        /// <summary>true = 생성, false = 취소. View 가 구독해 DialogResult 로 변환.</summary>
        public event EventHandler<bool>? CloseRequested;

        public TemplateGalleryViewModel()
        {
            // 카탈로그 정의 순서를 카테고리 순서로 유지 (3D → Deep Learning → 2D → 식별)
            Categories = RecipeTemplateCatalog.Templates
                .GroupBy(t => t.Category)
                .Select(g => new TemplateCategoryGroup { Name = g.Key, Templates = g.ToList() })
                .ToList();
        }

        private bool CanCreate() => SelectedTemplate != null;

        [RelayCommand(CanExecute = nameof(CanCreate))]
        private void Create() => CloseRequested?.Invoke(this, true);

        [RelayCommand]
        private void Cancel() => CloseRequested?.Invoke(this, false);
    }
}
