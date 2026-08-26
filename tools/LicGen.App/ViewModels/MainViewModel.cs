using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Boda.LicGen.App.Messages;
using Boda.LicGen.App.Services;

namespace Boda.LicGen.App.ViewModels
{
    /// <summary>상단 배너(키·백업 상태) + 발급/대장 탭 구성.</summary>
    public partial class MainViewModel : ObservableObject
    {
        public static string DefaultKeyDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".boda-licgen");

        private readonly IDialogService _dialogService;

        public IssueViewModel Issue { get; }
        public LedgerViewModel Ledger { get; }

        [ObservableProperty]
        private string _keyStatusText = "";

        [ObservableProperty]
        private bool _hasKeyWarning;

        [ObservableProperty]
        private string _backupStatusText = "";

        [ObservableProperty]
        private bool _hasBackupWarning;

        public MainViewModel(IDialogService dialogService, LedgerNotesStore notesStore)
        {
            _dialogService = dialogService;
            Issue = new IssueViewModel(DefaultKeyDir, dialogService);
            Ledger = new LedgerViewModel(DefaultKeyDir, notesStore, dialogService);

            WeakReferenceMessenger.Default.Register<MainViewModel, LicenseIssuedMessage>(
                this, static (self, _) => self.RefreshStatus());

            RefreshStatus();
        }

        [RelayCommand]
        private void RefreshStatus()
        {
            var keys = Issue.ReloadAvailableKeys();
            HasKeyWarning = keys.Count == 0;
            KeyStatusText = HasKeyWarning
                ? $"개인키 없음 — {DefaultKeyDir} 에 *.private.pem 이 있어야 발급 가능 (운영 절차 §1)"
                : $"서명 키: {string.Join(" · ", keys.Select(k => k.KeyId))}";

            var status = BackupService.GetStatus(DefaultKeyDir);
            if (!status.HasRecord)
            {
                HasBackupWarning = true;
                BackupStatusText = "백업 기록 없음 — 오프라인 USB 2부에 [백업 실행] 필요 (운영 절차 §1a)";
            }
            else
            {
                HasBackupWarning = status.PendingIssues > 0 || status.MediaCount < BackupService.RequiredCopies;
                BackupStatusText =
                    $"마지막 백업 {status.NewestUtc![..10]} · 매체 {status.MediaCount}부 · 백업 미반영 발급 {status.PendingIssues}건" +
                    (status.PendingIssues > 0 ? " — 발급 주 말일까지 2부 갱신" : "");
            }

            Ledger.Refresh();
        }

        [RelayCommand]
        private void RunBackup()
        {
            var target = _dialogService.ShowSelectBackupFolderDialog();
            if (target == null) return;

            try
            {
                var result = BackupService.Run(DefaultKeyDir, target);
                var notice = result.MediaCount < BackupService.RequiredCopies
                    ? "\n\n백업 규칙은 오프라인 매체 2부 — 다른 USB 에도 한 번 더 실행하세요."
                    : "";
                _dialogService.ShowInformation(
                    $"{result.FileCount}개 파일 → {result.TargetDir}\n매체: {result.MediaKey} (대장 {result.LedgerLines}건){notice}",
                    "백업 완료");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                _dialogService.ShowError(ex.Message, "백업 실패");
            }

            RefreshStatus();
        }
    }
}
