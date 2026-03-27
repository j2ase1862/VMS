using System;
using VMS.VisionSetup.VisionTools.Identification;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class OCRToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private OCRTool TypedTool => (OCRTool)Tool;

        public OCRToolSettingsViewModel(OCRTool tool) : base(tool) { }

        // ── Engine Type ──

        public OcrEngineType OcrEngine
        {
            get => TypedTool.OcrEngine;
            set { TypedTool.OcrEngine = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTesseract)); OnPropertyChanged(nameof(IsPPOcrOnnx)); }
        }

        public bool IsTesseract => OcrEngine == OcrEngineType.Tesseract;
        public bool IsPPOcrOnnx => OcrEngine == OcrEngineType.PPOcrOnnx;

        // ── PP-OCR ONNX Settings ──

        public int MaxSideLen
        {
            get => TypedTool.MaxSideLen;
            set { TypedTool.MaxSideLen = value; OnPropertyChanged(); }
        }

        public string CustomDetModelPath
        {
            get => TypedTool.CustomDetModelPath;
            set { TypedTool.CustomDetModelPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCustomModel)); }
        }

        public string CustomRecModelPath
        {
            get => TypedTool.CustomRecModelPath;
            set { TypedTool.CustomRecModelPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCustomModel)); }
        }

        public string CustomDictPath
        {
            get => TypedTool.CustomDictPath;
            set { TypedTool.CustomDictPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCustomModel)); }
        }

        public bool HasCustomModel => TypedTool.HasCustomModel;

        // ── Detection ──

        public OcrLanguage Language
        {
            get => TypedTool.Language;
            set { TypedTool.Language = value; OnPropertyChanged(); }
        }

        public OcrPageSegMode PageSegMode
        {
            get => TypedTool.PageSegMode;
            set { TypedTool.PageSegMode = value; OnPropertyChanged(); }
        }

        public OcrEngineMode EngineMode
        {
            get => TypedTool.EngineMode;
            set { TypedTool.EngineMode = value; OnPropertyChanged(); }
        }

        public string CharacterWhitelist
        {
            get => TypedTool.CharacterWhitelist;
            set { TypedTool.CharacterWhitelist = value; OnPropertyChanged(); }
        }

        public double ConfidenceThreshold
        {
            get => TypedTool.ConfidenceThreshold;
            set { TypedTool.ConfidenceThreshold = value; OnPropertyChanged(); }
        }

        // ── Preprocessing ──

        public bool AutoPreprocess
        {
            get => TypedTool.AutoPreprocess;
            set { TypedTool.AutoPreprocess = value; OnPropertyChanged(); }
        }

        public bool InvertImage
        {
            get => TypedTool.InvertImage;
            set { TypedTool.InvertImage = value; OnPropertyChanged(); }
        }

        public int TargetTextHeight
        {
            get => TypedTool.TargetTextHeight;
            set { TypedTool.TargetTextHeight = value; OnPropertyChanged(); }
        }

        public int DenoiseLevel
        {
            get => TypedTool.DenoiseLevel;
            set { TypedTool.DenoiseLevel = value; OnPropertyChanged(); }
        }

        public bool DotMatrixMode
        {
            get => TypedTool.DotMatrixMode;
            set { TypedTool.DotMatrixMode = value; OnPropertyChanged(); }
        }

        // ── Verification ──

        public bool EnableVerification
        {
            get => TypedTool.EnableVerification;
            set { TypedTool.EnableVerification = value; OnPropertyChanged(); }
        }

        public string ExpectedText
        {
            get => TypedTool.ExpectedText;
            set { TypedTool.ExpectedText = value; OnPropertyChanged(); }
        }

        public bool UseRegexMatch
        {
            get => TypedTool.UseRegexMatch;
            set { TypedTool.UseRegexMatch = value; OnPropertyChanged(); }
        }

        // ── Display ──

        public bool DrawOverlay
        {
            get => TypedTool.DrawOverlay;
            set { TypedTool.DrawOverlay = value; OnPropertyChanged(); }
        }

        // ── Tessdata ──

        public string TessdataPath
        {
            get => TypedTool.TessdataPath;
            set { TypedTool.TessdataPath = value; OnPropertyChanged(); }
        }
    }
}
