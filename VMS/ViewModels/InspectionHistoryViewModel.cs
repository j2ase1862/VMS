using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VMS.Services.LocalHistory;

namespace VMS.ViewModels
{
    /// <summary>
    /// 로컬 생산(검사) 이력 조회 창 ViewModel — inspection_history.db 를 기간·판정·레시피·NG 코드로
    /// 조회해 목록/상세(도구 결과 + 이미지)/일별 집계/NG 파레토를 보여 주고 CSV 로 내보낸다.
    /// 단독 모드의 생산 이력 화면이며, Web 연동 모드에서는 오프라인 보조 조회.
    /// </summary>
    public partial class InspectionHistoryViewModel : ObservableObject
    {
        public const string VerdictAll = "(전체)";
        public const string VerdictPass = "PASS";
        public const string VerdictNg = "NG";
        public const int DefaultPageSize = 200;
        private const int DetailImageDecodeWidth = 1280;

        private readonly ILocalInspectionHistoryStore _store;

        public IReadOnlyList<string> VerdictOptions { get; } = new[] { VerdictAll, VerdictPass, VerdictNg };
        public ObservableCollection<string?> RecipeOptions { get; } = new() { null };

        public ObservableCollection<LocalInspectionEntry> Entries { get; } = new();
        public ObservableCollection<LocalInspectionDailySummary> DailySummaries { get; } = new();
        public ObservableCollection<NgCodeRow> NgCodeRows { get; } = new();

        // ─── 필터 ───
        [ObservableProperty] private DateTime _fromDate = DateTime.Today.AddDays(-7);
        [ObservableProperty] private DateTime _toDate = DateTime.Today;
        [ObservableProperty] private string _selectedVerdict = VerdictAll;
        [ObservableProperty] private string? _selectedRecipe;
        [ObservableProperty] private string _ngCodeText = string.Empty;

        // ─── 페이징 ───
        [ObservableProperty] private int _pageSize = DefaultPageSize;
        [ObservableProperty] private int _pageIndex;          // 0-based
        [ObservableProperty] private long _totalCount;
        [ObservableProperty] private string _pageText = string.Empty;
        [ObservableProperty] private bool _canGoPrev;
        [ObservableProperty] private bool _canGoNext;

        // ─── 집계 ───
        [ObservableProperty] private int _summaryTotal;
        [ObservableProperty] private int _summaryPass;
        [ObservableProperty] private int _summaryNg;
        [ObservableProperty] private double _summaryPassRate;

        // ─── 상세 ───
        [ObservableProperty] private LocalInspectionEntry? _selectedEntry;
        [ObservableProperty] private BitmapSource? _detailImage;
        [ObservableProperty] private string _detailImageStatus = string.Empty;
        [ObservableProperty] private bool _hasDetail;
        [ObservableProperty] private bool _showNoDetailHint = true;
        [ObservableProperty] private bool _hasDetailImage;
        /// <summary>이미지 자리에 상태 문구를 겹쳐 보여줄지 (= 이미지 없음).</summary>
        [ObservableProperty] private bool _showImageStatusOverlay = true;

        partial void OnHasDetailImageChanged(bool value) => ShowImageStatusOverlay = !value;

        // ─── 상태 ───
        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private string _dbInfo = string.Empty;
        [ObservableProperty] private bool _isWebIntegrated;

        public InspectionHistoryViewModel(ILocalInspectionHistoryStore store, bool isWebIntegrated = false)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            IsWebIntegrated = isWebIntegrated;
            RefreshDbInfo();
            Search();
        }

        // 기간 경계 — ToDate 는 포함(그날 23:59:59.999 까지) 이므로 배타 상한은 다음날 00:00.
        private DateTime FromLocal => FromDate.Date;
        private DateTime ToLocalExclusive => ToDate.Date.AddDays(1);

        private LocalInspectionQuery BuildQuery(int offset, int limit) => new()
        {
            FromLocal = FromLocal,
            ToLocalExclusive = ToLocalExclusive,
            IsPass = SelectedVerdict == VerdictPass ? true : SelectedVerdict == VerdictNg ? false : null,
            RecipeName = string.IsNullOrWhiteSpace(SelectedRecipe) ? null : SelectedRecipe,
            NgCodeContains = string.IsNullOrWhiteSpace(NgCodeText) ? null : NgCodeText.Trim(),
            Offset = offset,
            Limit = limit
        };

        [RelayCommand]
        private void Search()
        {
            PageIndex = 0;
            LoadPage();
            LoadAggregates();
            RefreshRecipeOptions();
        }

        [RelayCommand]
        private void ResetFilters()
        {
            FromDate = DateTime.Today.AddDays(-7);
            ToDate = DateTime.Today;
            SelectedVerdict = VerdictAll;
            SelectedRecipe = null;
            NgCodeText = string.Empty;
            Search();
        }

        [RelayCommand]
        private void NextPage()
        {
            if (!CanGoNext) return;
            PageIndex++;
            LoadPage();
        }

        [RelayCommand]
        private void PrevPage()
        {
            if (!CanGoPrev) return;
            PageIndex--;
            LoadPage();
        }

        private void LoadPage()
        {
            try
            {
                if (FromLocal >= ToLocalExclusive)
                {
                    StatusMessage = "기간이 올바르지 않습니다 (From ≤ To).";
                    return;
                }
                var size = Math.Max(1, PageSize);
                TotalCount = _store.Count(BuildQuery(0, 1));
                var rows = _store.Query(BuildQuery(PageIndex * size, size));
                Entries.Clear();
                foreach (var r in rows) Entries.Add(r);
                SelectedEntry = Entries.FirstOrDefault();

                var pageCount = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)size);
                CanGoPrev = PageIndex > 0;
                CanGoNext = PageIndex < pageCount - 1;
                PageText = TotalCount == 0
                    ? "0건"
                    : $"{PageIndex * size + 1}–{PageIndex * size + Entries.Count} / {TotalCount:N0}건 (페이지 {PageIndex + 1}/{pageCount})";
                StatusMessage = $"{FromDate:yyyy-MM-dd} ~ {ToDate:yyyy-MM-dd}: {TotalCount:N0}건";
            }
            catch (Exception ex)
            {
                StatusMessage = $"조회 실패: {ex.Message}";
            }
        }

        private void LoadAggregates()
        {
            try
            {
                DailySummaries.Clear();
                var days = _store.GetDailySummary(FromLocal, ToLocalExclusive);
                int total = 0, pass = 0;
                foreach (var d in days)
                {
                    DailySummaries.Add(d);
                    total += d.Total;
                    pass += d.Pass;
                }
                SummaryTotal = total;
                SummaryPass = pass;
                SummaryNg = total - pass;
                SummaryPassRate = total > 0 ? Math.Round(pass * 100.0 / total, 1) : 0;

                NgCodeRows.Clear();
                var codes = _store.GetNgCodeCounts(FromLocal, ToLocalExclusive, top: 30);
                var codeTotal = codes.Sum(c => c.Count);
                var cumulative = 0;
                foreach (var c in codes)
                {
                    cumulative += c.Count;
                    NgCodeRows.Add(new NgCodeRow
                    {
                        Rank = NgCodeRows.Count + 1,
                        Code = c.Code,
                        Count = c.Count,
                        Percent = codeTotal > 0 ? Math.Round(c.Count * 100.0 / codeTotal, 1) : 0,
                        CumulativePercent = codeTotal > 0 ? Math.Round(cumulative * 100.0 / codeTotal, 1) : 0
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] aggregates: {ex.Message}");
            }
        }

        private void RefreshRecipeOptions()
        {
            try
            {
                var current = SelectedRecipe;
                var names = _store.GetRecipeNames(FromLocal, ToLocalExclusive);
                RecipeOptions.Clear();
                RecipeOptions.Add(null);
                foreach (var n in names) RecipeOptions.Add(n);
                SelectedRecipe = current != null && names.Contains(current) ? current : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistory] recipe options: {ex.Message}");
            }
        }

        private void RefreshDbInfo()
        {
            var mb = _store.FileSizeBytes / 1024.0 / 1024.0;
            DbInfo = $"{_store.DbPath}  ({mb:F1} MB)";
        }

        partial void OnSelectedEntryChanged(LocalInspectionEntry? value)
        {
            HasDetail = value != null;
            ShowNoDetailHint = value == null;
            LoadDetailImage(value);
        }

        private void LoadDetailImage(LocalInspectionEntry? entry)
        {
            DetailImage = null;
            HasDetailImage = false;
            if (entry == null) { DetailImageStatus = string.Empty; return; }
            if (string.IsNullOrEmpty(entry.ImagePath))
            {
                DetailImageStatus = "저장된 이미지 없음 (이미지 저장 설정에서 OK/NG 저장을 켜면 다음 검사부터 연결됩니다)";
                return;
            }
            if (!File.Exists(entry.ImagePath))
            {
                DetailImageStatus = $"이미지 파일이 없습니다 (보존 정리로 삭제됐거나 이동됨): {entry.ImagePath}";
                return;
            }
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // 파일 핸들 즉시 해제 (보존 정리와 충돌 방지)
                bmp.DecodePixelWidth = DetailImageDecodeWidth; // 5MP 원본을 그대로 올리지 않음
                bmp.UriSource = new Uri(entry.ImagePath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                DetailImage = bmp;
                HasDetailImage = true;
                DetailImageStatus = $"{(entry.ImageIsNg ? "NG" : "OK")} 이미지 · {entry.ImagePath}";
            }
            catch (Exception ex)
            {
                DetailImageStatus = $"이미지 열기 실패: {ex.Message}";
            }
        }

        [RelayCommand]
        private void OpenImageFolder()
        {
            var path = SelectedEntry?.ImagePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = $"폴더 열기 실패: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ExportCsv()
        {
            if (TotalCount == 0)
            {
                StatusMessage = "내보낼 항목이 없습니다 — 먼저 조회를 실행하세요.";
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                FileName = $"inspection_history_{FromDate:yyyyMMdd}_{ToDate:yyyyMMdd}.csv",
                DefaultExt = ".csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var count = ExportAllToCsv(dlg.FileName);
                StatusMessage = $"CSV 저장 완료 ({count:N0}건): {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"CSV 저장 실패: {ex.Message}";
            }
        }

        /// <summary>현재 필터의 **전체** 결과(페이지 무관)를 청크로 읽어 CSV 로 기록. 테스트 가능하도록 분리.</summary>
        internal long ExportAllToCsv(string outputPath)
        {
            const int chunk = 5000;
            long written = 0;
            using var writer = new StreamWriter(outputPath, false, new System.Text.UTF8Encoding(true));
            writer.WriteLine(string.Join(",", InspectionHistoryCsvExporter.Header));
            for (int offset = 0; ; offset += chunk)
            {
                var rows = _store.Query(BuildQuery(offset, chunk));
                foreach (var r in rows)
                {
                    writer.WriteLine(InspectionHistoryCsvExporter.ToCsvLine(r));
                    written++;
                }
                if (rows.Count < chunk) break;
            }
            return written;
        }
    }

    /// <summary>NG 파레토 1행.</summary>
    public sealed class NgCodeRow
    {
        public int Rank { get; init; }
        public string Code { get; init; } = string.Empty;
        public int Count { get; init; }
        public double Percent { get; init; }
        public double CumulativePercent { get; init; }
    }
}
