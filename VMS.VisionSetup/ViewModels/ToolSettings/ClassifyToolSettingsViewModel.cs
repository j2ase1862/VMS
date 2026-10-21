using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class ClassifyToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private ClassifyTool TypedTool => (ClassifyTool)Tool;
        private bool _syncingOkClasses;

        public ClassifyToolSettingsViewModel(ClassifyTool tool) : base(tool)
        {
            RebuildOkClassOptions();
        }

        // Model
        public string ModelPath { get => TypedTool.ModelPath; set => TypedTool.ModelPath = value; }
        public int InputWidth { get => TypedTool.InputWidth; set => TypedTool.InputWidth = value; }
        public int InputHeight { get => TypedTool.InputHeight; set => TypedTool.InputHeight = value; }

        // Classification
        public double ConfidenceThreshold { get => TypedTool.ConfidenceThreshold; set => TypedTool.ConfidenceThreshold = value; }
        public string ClassNamesText { get => TypedTool.ClassNamesText; set => TypedTool.ClassNamesText = value; }
        public bool UseImageNetNormalization { get => TypedTool.UseImageNetNormalization; set => TypedTool.UseImageNetNormalization = value; }
        public ObservableCollection<string> ModelClassNames => TypedTool.ModelClassNames;

        // 양품 클래스 — 클래스 이름마다 체크박스 하나
        public string OkClassesText { get => TypedTool.OkClassesText; set => TypedTool.OkClassesText = value; }
        public ObservableCollection<OkClassOption> OkClassOptions { get; } = new();
        public bool HasClassNames => OkClassOptions.Count > 0;
        public bool HasNoOkClasses => TypedTool.OkClasses.Length == 0;

        // Display
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set => TypedTool.DrawOverlay = value; }

        protected override void OnToolPropertyChanged(string? propertyName)
        {
            base.OnToolPropertyChanged(propertyName);
            if (propertyName == nameof(ClassifyTool.ClassNamesText) || propertyName == nameof(ClassifyTool.OkClassesText))
            {
                if (!_syncingOkClasses) RebuildOkClassOptions();
                OnPropertyChanged(nameof(HasNoOkClasses));
            }
        }

        private void RebuildOkClassOptions()
        {
            var ok = TypedTool.OkClasses;
            foreach (var o in OkClassOptions) o.PropertyChanged -= OnOptionChanged;
            OkClassOptions.Clear();
            foreach (var name in TypedTool.ClassNames)
            {
                var option = new OkClassOption(name)
                {
                    IsOk = ok.Contains(name, StringComparer.OrdinalIgnoreCase)
                };
                option.PropertyChanged += OnOptionChanged;
                OkClassOptions.Add(option);
            }
            OnPropertyChanged(nameof(HasClassNames));
        }

        private void OnOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(OkClassOption.IsOk)) return;
            _syncingOkClasses = true;
            try
            {
                // Class Names 순서로 기록한다
                TypedTool.OkClassesText = string.Join(", ", OkClassOptions.Where(o => o.IsOk).Select(o => o.Name));
            }
            finally
            {
                _syncingOkClasses = false;
            }
        }

        public override void Dispose()
        {
            foreach (var o in OkClassOptions) o.PropertyChanged -= OnOptionChanged;
            base.Dispose();
        }
    }

    /// <summary>OK Classes 체크박스 한 줄 — 모델 클래스 이름과 양품 여부.</summary>
    public partial class OkClassOption : ObservableObject
    {
        public OkClassOption(string name) => Name = name;

        public string Name { get; }

        [ObservableProperty]
        private bool _isOk;
    }
}
