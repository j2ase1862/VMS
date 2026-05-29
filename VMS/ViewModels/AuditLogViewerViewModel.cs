using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VMS.Core.Security;

namespace VMS.ViewModels
{
    /// <summary>
    /// 감사 로그 조회 윈도우용 ViewModel.
    /// 일자 범위 + 카테고리 + outcome + 검색 필터로 jsonl 파일들을 읽어 DataGrid 에 표시,
    /// CSV export 지원 (감사 심사 / 외부 분석용).
    /// </summary>
    public partial class AuditLogViewerViewModel : ObservableObject
    {
        /// <summary>필터 선택 콤보용 카테고리 옵션 (null 옵션 = 전체).</summary>
        public IReadOnlyList<AuditCategory?> CategoryOptions { get; }

        /// <summary>필터 선택 콤보용 outcome 옵션 (null 옵션 = 전체).</summary>
        public IReadOnlyList<AuditOutcome?> OutcomeOptions { get; }

        /// <summary>조회 결과.</summary>
        public ObservableCollection<AuditLogEntry> Entries { get; } = new();

        [ObservableProperty] private DateTime _fromDate = DateTime.Today.AddDays(-7);
        [ObservableProperty] private DateTime _toDate = DateTime.Today;
        [ObservableProperty] private AuditCategory? _selectedCategory;
        [ObservableProperty] private AuditOutcome? _selectedOutcome;
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private int _entryCount;

        public AuditLogViewerViewModel()
        {
            // 카테고리 / outcome 옵션 — 첫 항목 null 은 "전체" 의미.
            var cats = new List<AuditCategory?> { null };
            foreach (AuditCategory c in Enum.GetValues(typeof(AuditCategory))) cats.Add(c);
            CategoryOptions = cats;

            var outs = new List<AuditOutcome?> { null };
            foreach (AuditOutcome o in Enum.GetValues(typeof(AuditOutcome))) outs.Add(o);
            OutcomeOptions = outs;

            Search();
        }

        [RelayCommand]
        private void Search()
        {
            try
            {
                var entries = AuditLogger.Instance.ReadRange(
                    FromDate, ToDate, SelectedCategory, SelectedOutcome, SearchText);
                Entries.Clear();
                foreach (var e in entries) Entries.Add(e);
                EntryCount = Entries.Count;
                StatusMessage = $"{FromDate:yyyy-MM-dd} ~ {ToDate:yyyy-MM-dd}: {EntryCount}건";
            }
            catch (Exception ex)
            {
                StatusMessage = $"조회 실패: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ResetFilters()
        {
            FromDate = DateTime.Today.AddDays(-7);
            ToDate = DateTime.Today;
            SelectedCategory = null;
            SelectedOutcome = null;
            SearchText = string.Empty;
            Search();
        }

        [RelayCommand]
        private void ExportCsv()
        {
            if (Entries.Count == 0)
            {
                StatusMessage = "내보낼 항목이 없습니다 — 먼저 조회를 실행하세요.";
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                FileName = $"audit_{FromDate:yyyyMMdd}_{ToDate:yyyyMMdd}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                AuditLogger.ExportToCsv(Entries, dlg.FileName);
                StatusMessage = $"CSV 저장 완료: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"CSV 저장 실패: {ex.Message}";
            }
        }
    }
}
