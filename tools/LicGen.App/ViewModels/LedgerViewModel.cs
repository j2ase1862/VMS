using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Boda.LicGen.App.Services;

namespace Boda.LicGen.App.ViewModels
{
    /// <summary>대장 표시용 행 — 원본 LedgerEntry + 비고 (표시 날짜는 로컬).</summary>
    public sealed record LedgerRow(
        string LicenseId,
        string Customer,
        string Kind,
        string Fingerprint,
        int MaxClients,
        string MaintenanceUntil,
        string ExpiresAt,
        string IssuedAt,
        string Issuer,
        string Note);

    /// <summary>
    /// 대장 탭 — ledger.jsonl 은 읽기 전용(append-only 원칙), 비고만 별도 파일에 편집.
    /// </summary>
    public partial class LedgerViewModel : ObservableObject
    {
        private readonly string _keyDir;
        private readonly LedgerNotesStore _notesStore;
        private readonly IDialogService _dialogService;

        public ObservableCollection<LedgerRow> Rows { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveNoteCommand))]
        private LedgerRow? _selectedRow;

        [ObservableProperty]
        private string _noteText = "";

        [ObservableProperty]
        private string _summaryText = "";

        public LedgerViewModel(string keyDir, LedgerNotesStore notesStore, IDialogService dialogService)
        {
            _keyDir = keyDir;
            _notesStore = notesStore;
            _dialogService = dialogService;
            Refresh();
        }

        partial void OnSelectedRowChanged(LedgerRow? value) => NoteText = value?.Note ?? "";

        public void Refresh()
        {
            var selectedId = SelectedRow?.LicenseId;
            var notes = _notesStore.ReadAll();
            var entries = LedgerStore.Read(_keyDir);

            Rows.Clear();
            foreach (var e in entries.Reverse()) // 최신이 위
            {
                Rows.Add(new LedgerRow(
                    e.LicenseId, e.Customer, e.Kind, e.Fingerprint, e.MaxClients,
                    e.MaintenanceUntil ?? "-",
                    e.ExpiresAt ?? "영구",
                    DateTime.TryParse(e.IssuedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var utc)
                        ? utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : e.IssuedAtUtc,
                    e.Issuer,
                    notes.GetValueOrDefault(e.LicenseId, "")));
            }

            SummaryText = $"총 {Rows.Count}건 — {LedgerStore.PathFor(_keyDir)}";
            SelectedRow = Rows.FirstOrDefault(r => r.LicenseId == selectedId) ?? Rows.FirstOrDefault();
        }

        private bool CanSaveNote() => SelectedRow != null;

        [RelayCommand(CanExecute = nameof(CanSaveNote))]
        private void SaveNote()
        {
            if (SelectedRow == null) return;
            try
            {
                _notesStore.Save(SelectedRow.LicenseId, NoteText);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                _dialogService.ShowError(ex.Message, "비고 저장 실패");
                return;
            }
            Refresh();
        }
    }
}
