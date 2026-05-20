using System;
using VMS.VisionSetup.VisionTools.CodeReading;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class CodeReaderToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private CodeReaderTool TypedTool => (CodeReaderTool)Tool;

        public CodeReaderToolSettingsViewModel(CodeReaderTool tool) : base(tool) { }

        // Detection
        public CodeReaderMode CodeReaderMode { get => TypedTool.CodeReaderMode; set => TypedTool.CodeReaderMode = value; }
        public int MaxCodeCount { get => TypedTool.MaxCodeCount; set => TypedTool.MaxCodeCount = value; }
        public bool TryHarder { get => TypedTool.TryHarder; set => TypedTool.TryHarder = value; }
        public bool UseLocalization { get => TypedTool.UseLocalization; set { TypedTool.UseLocalization = value; OnPropertyChanged(); } }

        // Verification
        public bool EnableVerification { get => TypedTool.EnableVerification; set => TypedTool.EnableVerification = value; }
        public string ExpectedText { get => TypedTool.ExpectedText; set => TypedTool.ExpectedText = value; }
        public bool UseRegexMatch { get => TypedTool.UseRegexMatch; set => TypedTool.UseRegexMatch = value; }

        // GS1
        public bool ParseGs1 { get => TypedTool.ParseGs1; set { TypedTool.ParseGs1 = value; OnPropertyChanged(); } }

        // Quality Grading
        public bool EnableQualityGrading
        {
            get => TypedTool.EnableQualityGrading;
            set { TypedTool.EnableQualityGrading = value; OnPropertyChanged(); }
        }
        public CodeQualityGrade MinPassGrade
        {
            get => TypedTool.MinPassGrade;
            set { TypedTool.MinPassGrade = value; OnPropertyChanged(); }
        }

        // Display
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set => TypedTool.DrawOverlay = value; }

        // Enum value arrays for ComboBox binding
        public Array CodeReaderModes => Enum.GetValues(typeof(CodeReaderMode));
        public Array PassGrades => Enum.GetValues(typeof(CodeQualityGrade));
    }
}
