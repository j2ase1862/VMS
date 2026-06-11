using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace VMS.Core.Imaging
{
    /// <summary>
    /// 검사 판정 이미지(양품/불량) 저장 설정 — system_config.json 의 "imageSave"
    /// 객체에서 로드. 키 누락 / 파일 없음 시 기본값 + 저장 비활성.
    ///
    /// 폴더 구조: {BaseDir}\{yyyy-MM-dd}\{OK|NG}\{파일명}.{확장자}
    /// 파일명: FileNameTokens 의 활성 토큰을 순서대로 Separator 로 연결(빈 값은 건너뜀).
    /// </summary>
    public sealed class ImageSaveOptions
    {
        public const int DefaultJpegQuality = 90;
        public const int MinJpegQuality = 1;
        public const int MaxJpegQuality = 100;
        public const ImageSaveFormat DefaultFormat = ImageSaveFormat.Png;
        public const string DefaultSeparator = "_";
        public const string DefaultTimestampFormat = "yyyyMMdd_HHmmss_fff";
        public const int DefaultRetentionDays = 0;   // 0 = 무제한(자동 삭제 안 함)
        public const int MaxRetentionDays = 3650;    // 10년 상한 — 손상/오입력 보호
        public const int DefaultThumbnailMaxEdge = 1024;  // 장변 px — Web 전송 썸네일
        public const int MinThumbnailMaxEdge = 128;
        public const int MaxThumbnailMaxEdge = 8192;

        /// <summary>양품(OK) 이미지를 디스크에 저장할지 여부.</summary>
        public bool SaveOkImages { get; init; }

        /// <summary>불량(NG) 이미지를 디스크에 저장할지 여부.</summary>
        public bool SaveNgImages { get; init; }

        /// <summary>
        /// 저장 루트 폴더. 비어 있으면 저장 비활성과 동일. 실제 저장 경로는
        /// {BaseDir}\{연월일}\{OK|NG} 으로 자동 구성.
        /// </summary>
        public string BaseDir { get; init; } = string.Empty;

        /// <summary>저장 파일 포맷 (PNG / JPEG / BMP / TIFF).</summary>
        public ImageSaveFormat Format { get; init; } = DefaultFormat;

        /// <summary>JPEG 품질 (1–100). JPEG 포맷에서만 사용.</summary>
        public int JpegQuality { get; init; } = DefaultJpegQuality;

        /// <summary>파일명 토큰 사이 구분자. 기본 "_".</summary>
        public string Separator { get; init; } = DefaultSeparator;

        /// <summary>타임스탬프 토큰 포맷. 기본 "yyyyMMdd_HHmmss_fff".</summary>
        public string TimestampFormat { get; init; } = DefaultTimestampFormat;

        /// <summary>
        /// 이미지 보존 기간(일). 0 이면 무제한(자동 삭제 안 함). 양수면 BaseDir 아래
        /// 연월일 폴더 중 기준일보다 오래된 것을 자동 삭제.
        /// </summary>
        public int RetentionDays { get; init; } = DefaultRetentionDays;

        // ── Web 연동 (BODA.VMS.Web) ──────────────────────────────

        /// <summary>
        /// 이미지 전달 모드. Auto = Web 호스트가 자기 머신이면 SharedPath(무전송),
        /// 아니면 Upload. SharedPath/Upload 로 수동 고정 가능.
        /// </summary>
        public ImageDeliveryMode DeliveryMode { get; init; } = ImageDeliveryMode.Auto;

        /// <summary>양품(OK) 이미지를 Web 으로 전송할지 여부 (Upload 모드에서만 의미).</summary>
        public bool WebSendOk { get; init; }

        /// <summary>불량(NG) 이미지를 Web 으로 전송할지 여부 (Upload 모드에서만 의미).</summary>
        public bool WebSendNg { get; init; }

        /// <summary>Web 전송 화질 — 풀(원본) 또는 썸네일. 기본 썸네일(대역폭/저장 절감).</summary>
        public WebImageVariant WebImageVariant { get; init; } = WebImageVariant.Thumbnail;

        /// <summary>썸네일 장변 px (Web 전송용). 기본 1024.</summary>
        public int ThumbnailMaxEdge { get; init; } = DefaultThumbnailMaxEdge;

        /// <summary>파일명 규칙 — 토큰 순서 + on/off.</summary>
        public List<FileNameTokenSetting> FileNameTokens { get; init; } = DefaultTokens();

        /// <summary>기본 파일명 규칙 — 판정/카메라/타임스탬프만 활성(기존 동작과 동일).</summary>
        public static List<FileNameTokenSetting> DefaultTokens() => new()
        {
            new FileNameTokenSetting { Token = FileNameToken.Verdict,   Enabled = true  },
            new FileNameTokenSetting { Token = FileNameToken.Camera,    Enabled = true  },
            new FileNameTokenSetting { Token = FileNameToken.Step,      Enabled = false },
            new FileNameTokenSetting { Token = FileNameToken.Recipe,    Enabled = false },
            new FileNameTokenSetting { Token = FileNameToken.WorkOrder, Enabled = false },
            new FileNameTokenSetting { Token = FileNameToken.Lot,       Enabled = false },
            new FileNameTokenSetting { Token = FileNameToken.Serial,    Enabled = false },
            new FileNameTokenSetting { Token = FileNameToken.Timestamp, Enabled = true  },
        };

        /// <summary>
        /// system_config.json 의 "imageSave" 객체에서 로드. 키 누락 / 파일 없음 시
        /// 기본값 + 저장 비활성. 손상 시 안전 fallback(저장 비활성).
        /// </summary>
        public static ImageSaveOptions LoadFromAppData()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var path = Path.Combine(appData, "BODA VISION AI", "system_config.json");
                if (!File.Exists(path)) return new ImageSaveOptions();

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("imageSave", out var elem)
                    || elem.ValueKind != JsonValueKind.Object)
                {
                    return new ImageSaveOptions();
                }

                bool saveOk = elem.TryGetProperty("saveOkImages", out var okProp) && okProp.ValueKind == JsonValueKind.True;
                bool saveNg = elem.TryGetProperty("saveNgImages", out var ngProp) && ngProp.ValueKind == JsonValueKind.True;

                string baseDir = string.Empty;
                if (elem.TryGetProperty("baseDir", out var bdProp) && bdProp.ValueKind == JsonValueKind.String)
                    baseDir = bdProp.GetString() ?? string.Empty;

                var format = DefaultFormat;
                if (elem.TryGetProperty("format", out var fmtProp) && fmtProp.ValueKind == JsonValueKind.String)
                    format = ParseFormat(fmtProp.GetString());

                int quality = DefaultJpegQuality;
                if (elem.TryGetProperty("jpegQuality", out var qProp) && qProp.TryGetInt32(out var qVal))
                    quality = ClampQuality(qVal);

                string separator = DefaultSeparator;
                if (elem.TryGetProperty("separator", out var sepProp) && sepProp.ValueKind == JsonValueKind.String)
                    separator = sepProp.GetString() ?? DefaultSeparator;

                string tsFormat = DefaultTimestampFormat;
                if (elem.TryGetProperty("timestampFormat", out var tsProp) && tsProp.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(tsProp.GetString()))
                    tsFormat = tsProp.GetString()!;

                int retentionDays = DefaultRetentionDays;
                if (elem.TryGetProperty("retentionDays", out var rdProp) && rdProp.TryGetInt32(out var rdVal))
                    retentionDays = ClampRetention(rdVal);

                var deliveryMode = ImageDeliveryMode.Auto;
                if (elem.TryGetProperty("deliveryMode", out var dmProp) && dmProp.ValueKind == JsonValueKind.String
                    && Enum.TryParse<ImageDeliveryMode>(dmProp.GetString(), ignoreCase: true, out var dmVal))
                    deliveryMode = dmVal;

                bool webSendOk = elem.TryGetProperty("webSendOk", out var wsoProp) && wsoProp.ValueKind == JsonValueKind.True;
                bool webSendNg = elem.TryGetProperty("webSendNg", out var wsnProp) && wsnProp.ValueKind == JsonValueKind.True;

                var webVariant = WebImageVariant.Thumbnail;
                if (elem.TryGetProperty("webImageVariant", out var wvProp) && wvProp.ValueKind == JsonValueKind.String
                    && Enum.TryParse<WebImageVariant>(wvProp.GetString(), ignoreCase: true, out var wvVal))
                    webVariant = wvVal;

                int thumbEdge = DefaultThumbnailMaxEdge;
                if (elem.TryGetProperty("thumbnailMaxEdge", out var teProp) && teProp.TryGetInt32(out var teVal))
                    thumbEdge = ClampThumbnailEdge(teVal);

                var tokens = ParseTokens(elem);

                return new ImageSaveOptions
                {
                    SaveOkImages = saveOk,
                    SaveNgImages = saveNg,
                    BaseDir = baseDir,
                    Format = format,
                    JpegQuality = quality,
                    Separator = separator,
                    TimestampFormat = tsFormat,
                    RetentionDays = retentionDays,
                    DeliveryMode = deliveryMode,
                    WebSendOk = webSendOk,
                    WebSendNg = webSendNg,
                    WebImageVariant = webVariant,
                    ThumbnailMaxEdge = thumbEdge,
                    FileNameTokens = tokens
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageSaveOptions] Load 실패 — 저장 비활성 fallback: {ex.Message}");
                return new ImageSaveOptions();
            }
        }

        /// <summary>
        /// "fileNameTokens" 배열([{token,enabled}, …]) 파싱. 누락/손상 시 기본 규칙.
        /// 알 수 없는 토큰은 무시하고, 누락된 토큰은 비활성으로 보강해 항상 전체 토큰을 보장.
        /// </summary>
        private static List<FileNameTokenSetting> ParseTokens(JsonElement imageSaveElem)
        {
            if (!imageSaveElem.TryGetProperty("fileNameTokens", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                return DefaultTokens();
            }

            var result = new List<FileNameTokenSetting>();
            var seen = new HashSet<FileNameToken>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("token", out var tProp) || tProp.ValueKind != JsonValueKind.String) continue;
                if (!Enum.TryParse<FileNameToken>(tProp.GetString(), ignoreCase: true, out var token)) continue;
                if (!seen.Add(token)) continue;
                bool enabled = item.TryGetProperty("enabled", out var eProp) && eProp.ValueKind == JsonValueKind.True;
                result.Add(new FileNameTokenSetting { Token = token, Enabled = enabled });
            }

            if (result.Count == 0) return DefaultTokens();

            // 새 버전에서 토큰이 추가됐을 때를 대비해 누락 토큰을 비활성으로 끝에 보강.
            foreach (var def in DefaultTokens())
                if (seen.Add(def.Token))
                    result.Add(new FileNameTokenSetting { Token = def.Token, Enabled = false });

            return result;
        }

        /// <summary>
        /// 활성 토큰을 순서대로 Separator 로 연결해 파일명(확장자 제외) 생성.
        /// <paramref name="values"/> 에서 각 토큰 값을 조회하며, 빈 값은 토큰과 함께 건너뛴다.
        /// 모든 값이 비면 "image" 로 폴백해 빈 파일명을 방지.
        /// </summary>
        public string BuildFileName(IReadOnlyDictionary<FileNameToken, string?> values)
        {
            var parts = new List<string>();
            foreach (var setting in FileNameTokens)
            {
                if (!setting.Enabled) continue;
                values.TryGetValue(setting.Token, out var raw);
                var clean = SanitizePart(raw);
                if (!string.IsNullOrEmpty(clean)) parts.Add(clean);
            }

            if (parts.Count == 0) return "image";
            var sep = SanitizePart(Separator) ?? string.Empty;
            return string.Join(sep, parts);
        }

        public static int ClampQuality(int quality)
        {
            if (quality < MinJpegQuality) return MinJpegQuality;
            if (quality > MaxJpegQuality) return MaxJpegQuality;
            return quality;
        }

        public static int ClampRetention(int days)
        {
            if (days < 0) return 0;
            if (days > MaxRetentionDays) return MaxRetentionDays;
            return days;
        }

        public static int ClampThumbnailEdge(int px)
        {
            if (px < MinThumbnailMaxEdge) return MinThumbnailMaxEdge;
            if (px > MaxThumbnailMaxEdge) return MaxThumbnailMaxEdge;
            return px;
        }

        /// <summary>대소문자 무시 파싱 — 알 수 없는 값은 기본 포맷(PNG).</summary>
        public static ImageSaveFormat ParseFormat(string? value)
        {
            return Enum.TryParse<ImageSaveFormat>(value, ignoreCase: true, out var parsed)
                ? parsed
                : DefaultFormat;
        }

        /// <summary>포맷별 소문자 파일 확장자 (점 제외).</summary>
        public static string ExtensionFor(ImageSaveFormat format) => format switch
        {
            ImageSaveFormat.Jpeg => "jpg",
            ImageSaveFormat.Bmp => "bmp",
            ImageSaveFormat.Tiff => "tiff",
            _ => "png",
        };

        /// <summary>UI 표시용 한글 라벨.</summary>
        public static string DisplayName(FileNameToken token) => token switch
        {
            FileNameToken.Verdict => "판정 (OK/NG)",
            FileNameToken.Camera => "카메라명",
            FileNameToken.Step => "스텝 번호",
            FileNameToken.Recipe => "레시피명",
            FileNameToken.WorkOrder => "작업지시 (WO)",
            FileNameToken.Lot => "Lot",
            FileNameToken.Serial => "S/N",
            FileNameToken.Timestamp => "타임스탬프",
            _ => token.ToString(),
        };

        /// <summary>파일명 부분에서 경로 불가 문자를 제거. 앞뒤 공백 trim.</summary>
        private static string? SanitizePart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var sb = new StringBuilder(value.Length);
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in value)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString().Trim();
        }
    }

    /// <summary>저장 이미지 파일 포맷.</summary>
    public enum ImageSaveFormat
    {
        Png,
        Jpeg,
        Bmp,
        Tiff
    }

    /// <summary>이미지 전달 모드 — Web 으로 보낼지/공유 경로로 둘지.</summary>
    public enum ImageDeliveryMode
    {
        /// <summary>Web 호스트가 자기 머신이면 SharedPath, 아니면 Upload 로 자동 판단.</summary>
        Auto,
        /// <summary>같은 머신/공유 경로 — Web 이 로컬 경로를 직접 읽으므로 전송 안 함.</summary>
        SharedPath,
        /// <summary>다른 머신 — Web 으로 업로드.</summary>
        Upload
    }

    /// <summary>Web 전송 이미지 화질.</summary>
    public enum WebImageVariant
    {
        /// <summary>원본 그대로.</summary>
        Full,
        /// <summary>장변 ThumbnailMaxEdge 로 축소(JPEG).</summary>
        Thumbnail
    }

    /// <summary>파일명을 구성하는 토큰 종류.</summary>
    public enum FileNameToken
    {
        Verdict,
        Camera,
        Step,
        Recipe,
        WorkOrder,
        Lot,
        Serial,
        Timestamp
    }

    /// <summary>파일명 규칙의 단일 토큰 — 종류 + 활성 여부. 순서는 리스트 순서로 표현.</summary>
    public sealed class FileNameTokenSetting
    {
        public FileNameToken Token { get; init; }
        public bool Enabled { get; init; }
    }

    /// <summary>
    /// 저장 시점에 파일명/폴더를 구성하기 위한 런타임 검사 컨텍스트.
    /// WPF 비의존 — VMS.Core 에 위치.
    /// </summary>
    public sealed class InspectionImageContext
    {
        public bool Ok { get; init; }
        public string CameraName { get; init; } = string.Empty;
        public int StepNumber { get; init; }
        /// <summary>결과 업로드와 공유하는 상관 키(Web 매칭용). null 이면 업로더가 자체 생성.</summary>
        public string? CorrelationKey { get; init; }
        public string RecipeName { get; init; } = string.Empty;
        public string WorkOrder { get; init; } = string.Empty;
        public string Lot { get; init; } = string.Empty;
        public string Serial { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }

        /// <summary>이 컨텍스트와 옵션으로 토큰→값 매핑을 구성.</summary>
        public IReadOnlyDictionary<FileNameToken, string?> ToTokenValues(ImageSaveOptions options)
        {
            return new Dictionary<FileNameToken, string?>
            {
                [FileNameToken.Verdict] = Ok ? "OK" : "NG",
                [FileNameToken.Camera] = CameraName,
                [FileNameToken.Step] = StepNumber > 0 ? StepNumber.ToString() : null,
                [FileNameToken.Recipe] = RecipeName,
                [FileNameToken.WorkOrder] = WorkOrder,
                [FileNameToken.Lot] = Lot,
                [FileNameToken.Serial] = Serial,
                [FileNameToken.Timestamp] = SafeTimestamp(options.TimestampFormat),
            };
        }

        private string SafeTimestamp(string format)
        {
            try { return Timestamp.ToString(format); }
            catch { return Timestamp.ToString(ImageSaveOptions.DefaultTimestampFormat); }
        }
    }
}
