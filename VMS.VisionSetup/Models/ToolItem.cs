using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 드래그 앤 드롭 가능한 도구 아이템
    /// </summary>
    public class ToolItem : ObservableObject
    {
        /// <summary>
        /// 도구 고유 ID (연결선 식별용)
        /// </summary>
        public string Id { get; } = Guid.NewGuid().ToString();

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _toolType = string.Empty;
        public string ToolType
        {
            get => _toolType;
            set => SetProperty(ref _toolType, value);
        }

        private double _x;
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        private double _y;
        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private VisionToolBase? _visionTool;
        public VisionToolBase? VisionTool
        {
            get => _visionTool;
            set => SetProperty(ref _visionTool, value);
        }

        // 아이콘 경로 (옵션)
        public string? IconPath { get; set; }
    }

    /// <summary>
    /// 도구 카테고리 (트리뷰용)
    /// </summary>
    public class ToolCategory : ObservableObject
    {
        private string _categoryName = string.Empty;
        public string CategoryName
        {
            get => _categoryName;
            set => SetProperty(ref _categoryName, value);
        }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public ObservableCollection<ToolItem> Tools { get; set; } = new();
    }
}
