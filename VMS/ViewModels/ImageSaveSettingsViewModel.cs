using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VMS.Core.Imaging;
using VMS.Core.Security;

namespace VMS.ViewModels
{
    /// <summary>
    /// 검사 판정 이미지 저장 설정 윈도우 ViewModel.
    /// system_config.json 의 "imageSave" 키만 읽고 쓰는 격리 편집 — 다른 키는 보존.
    /// 통합 경로({지정경로}\{연월일}\{OK|NG}) + 토큰 기반 파일명 규칙 + 실시간 예시.
    /// </summary>
    public partial class ImageSaveSettingsViewModel : ObservableObject
    {
        private readonly string _configPath;

        public ImageSaveSettingsViewModel()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI");
            _configPath = Path.Combine(appData, "system_config.json");
            Tokens.CollectionChanged += (_, _) => UpdatePreview();
            Load();
        }

        // ─── 바인딩 ───────────────────────────────────────────────

        [ObservableProperty] private bool _saveOkImages;
        [ObservableProperty] private bool _saveNgImages;
        [ObservableProperty] private string _baseDir = string.Empty;
        [ObservableProperty] private ImageSaveFormat _selectedFormat = ImageSaveOptions.DefaultFormat;
        [ObservableProperty] private int _jpegQuality = ImageSaveOptions.DefaultJpegQuality;
        [ObservableProperty] private string _separator = ImageSaveOptions.DefaultSeparator;

        [ObservableProperty] private string _previewFileName = string.Empty;
        [ObservableProperty] private string _previewPath = string.Empty;

        [ObservableProperty] private string _statusMessage = string.Empty;
        // WindowStyles.xaml 색 토큰과 일치 — BrushNeutral / BrushSuccess / BrushDanger.
        [ObservableProperty] private string _statusColor = "#22303C";

        /// <summary>파일명 규칙 토큰 — 순서 = 컬렉션 순서, on/off = Enabled.</summary>
        public ObservableCollection<FileNameTokenItem> Tokens { get; } = new();

        /// <summary>ComboBox 항목 소스 — 지원 포맷 전체.</summary>
        public ImageSaveFormat[] AvailableFormats { get; } =
            (ImageSaveFormat[])Enum.GetValues(typeof(ImageSaveFormat));

        // 변경 시 즉시 예시 갱신.
        partial void OnSelectedFormatChanged(ImageSaveFormat value) => UpdatePreview();
        partial void OnSeparatorChanged(string value) => UpdatePreview();
        partial void OnBaseDirChanged(string value) => UpdatePreview();

        // ─── 동작 ─────────────────────────────────────────────────

        [RelayCommand]
        private void Load()
        {
            try
            {
                var loaded = ImageSaveOptions.LoadFromAppData();
                SaveOkImages = loaded.SaveOkImages;
                SaveNgImages = loaded.SaveNgImages;
                BaseDir = loaded.BaseDir;
                SelectedFormat = loaded.Format;
                JpegQuality = loaded.JpegQuality;
                Separator = loaded.Separator;

                RebuildTokens(loaded.FileNameTokens);

                StatusMessage = File.Exists(_configPath)
                    ? "현재 system_config.json 값을 로드함"
                    : "system_config.json 없음 — 저장 시 새로 생성";
                StatusColor = "#22303C";  // BrushNeutral
                UpdatePreview();
            }
            catch (Exception ex)
            {
                StatusMessage = $"로드 실패: {ex.Message}";
                StatusColor = "#EF4444";  // BrushDanger
            }
        }

        [RelayCommand]
        private void PickBaseDir()
        {
            var dlg = new OpenFolderDialog { Title = "이미지 저장 루트 폴더 선택" };
            if (dlg.ShowDialog() == true) BaseDir = dlg.FolderName;
        }

        [RelayCommand]
        private void MoveTokenUp(FileNameTokenItem? item)
        {
            if (item == null) return;
            var idx = Tokens.IndexOf(item);
            if (idx > 0) Tokens.Move(idx, idx - 1);
        }

        [RelayCommand]
        private void MoveTokenDown(FileNameTokenItem? item)
        {
            if (item == null) return;
            var idx = Tokens.IndexOf(item);
            if (idx >= 0 && idx < Tokens.Count - 1) Tokens.Move(idx, idx + 1);
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                var q = ImageSaveOptions.ClampQuality(JpegQuality);
                if (q != JpegQuality) JpegQuality = q;

                // 다른 키 보존을 위해 JsonNode 로 read-modify-write.
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);

                JsonObject root;
                if (File.Exists(_configPath))
                {
                    var parsed = JsonNode.Parse(File.ReadAllText(_configPath));
                    root = parsed as JsonObject ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                var imageSave = root["imageSave"] as JsonObject ?? new JsonObject();
                imageSave["saveOkImages"] = SaveOkImages;
                imageSave["saveNgImages"] = SaveNgImages;
                imageSave["format"] = SelectedFormat.ToString();
                imageSave["jpegQuality"] = JpegQuality;
                imageSave["separator"] = Separator ?? ImageSaveOptions.DefaultSeparator;
                imageSave["timestampFormat"] = ImageSaveOptions.DefaultTimestampFormat;
                if (!string.IsNullOrWhiteSpace(BaseDir))
                    imageSave["baseDir"] = BaseDir;
                else
                    imageSave.Remove("baseDir");

                var tokenArray = new JsonArray();
                foreach (var t in Tokens)
                {
                    tokenArray.Add(new JsonObject
                    {
                        ["token"] = t.Token.ToString(),
                        ["enabled"] = t.Enabled
                    });
                }
                imageSave["fileNameTokens"] = tokenArray;
                root["imageSave"] = imageSave;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_configPath, root.ToJsonString(options));

                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "ImageSaveConfigSaved", AuditOutcome.Success,
                    source: nameof(ImageSaveSettingsViewModel),
                    details: $"OK={SaveOkImages}, NG={SaveNgImages}, Base={(string.IsNullOrEmpty(BaseDir) ? "-" : BaseDir)}, " +
                             $"Format={SelectedFormat}, JpegQuality={JpegQuality}, " +
                             $"Rule={string.Join("|", Tokens.Where(t => t.Enabled).Select(t => t.Token))}");

                StatusMessage = "저장됨 — 검사 이미지 저장 설정이 적용됩니다.";
                StatusColor = "#10B981";  // BrushSuccess
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageSaveSettings] Save 실패: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.Configuration, "ImageSaveConfigSaved", AuditOutcome.Failure,
                    source: nameof(ImageSaveSettingsViewModel),
                    details: $"{ex.GetType().Name}: {ex.Message}");
                StatusMessage = $"저장 실패: {ex.Message}";
                StatusColor = "#EF4444";  // BrushDanger
            }
        }

        // ─── 내부 ─────────────────────────────────────────────────

        private void RebuildTokens(System.Collections.Generic.IEnumerable<FileNameTokenSetting> settings)
        {
            foreach (var t in Tokens) t.PropertyChanged -= OnTokenItemChanged;
            Tokens.Clear();
            foreach (var s in settings)
            {
                var item = new FileNameTokenItem(s.Token) { Enabled = s.Enabled };
                item.PropertyChanged += OnTokenItemChanged;
                Tokens.Add(item);
            }
        }

        private void OnTokenItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileNameTokenItem.Enabled)) UpdatePreview();
        }

        /// <summary>현재 UI 상태로부터 옵션 객체 구성 — 예시/저장 로직 공유.</summary>
        private ImageSaveOptions BuildOptionsFromState() => new()
        {
            SaveOkImages = SaveOkImages,
            SaveNgImages = SaveNgImages,
            BaseDir = BaseDir,
            Format = SelectedFormat,
            JpegQuality = JpegQuality,
            Separator = Separator ?? ImageSaveOptions.DefaultSeparator,
            TimestampFormat = ImageSaveOptions.DefaultTimestampFormat,
            FileNameTokens = Tokens
                .Select(t => new FileNameTokenSetting { Token = t.Token, Enabled = t.Enabled })
                .ToList()
        };

        /// <summary>샘플 값으로 실제 저장 로직과 동일하게 예시 파일명/경로를 계산.</summary>
        private void UpdatePreview()
        {
            try
            {
                var opts = BuildOptionsFromState();
                var sample = new InspectionImageContext
                {
                    Ok = true,
                    CameraName = "Camera1",
                    StepNumber = 1,
                    RecipeName = "ProductA",
                    WorkOrder = "WO-1024",
                    Lot = "L01",
                    Serial = "SN0007",
                    Timestamp = DateTime.Now
                };
                var name = opts.BuildFileName(sample.ToTokenValues(opts));
                var ext = ImageSaveOptions.ExtensionFor(opts.Format);
                PreviewFileName = $"{name}.{ext}";

                var baseShown = string.IsNullOrWhiteSpace(BaseDir) ? "<저장 경로>" : BaseDir;
                PreviewPath = Path.Combine(baseShown, DateTime.Now.ToString("yyyy-MM-dd"), "OK", PreviewFileName);
            }
            catch (Exception ex)
            {
                PreviewFileName = $"(예시 계산 실패: {ex.Message})";
                PreviewPath = string.Empty;
            }
        }
    }

    /// <summary>파일명 규칙의 단일 토큰 — UI 바인딩용(체크 + 표시명).</summary>
    public partial class FileNameTokenItem : ObservableObject
    {
        public FileNameToken Token { get; }
        public string DisplayName { get; }

        [ObservableProperty] private bool _enabled;

        public FileNameTokenItem(FileNameToken token)
        {
            Token = token;
            DisplayName = ImageSaveOptions.DisplayName(token);
        }
    }
}
