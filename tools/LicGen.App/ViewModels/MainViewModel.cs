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
        private readonly string _keyDir;

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

        /// <param name="keyDir">키 폴더 — 생략하면 실제 키 폴더(DefaultKeyDir). 시험은 임시 폴더를 넘긴다.</param>
        public MainViewModel(IDialogService dialogService, LedgerNotesStore notesStore, string? keyDir = null)
        {
            _dialogService = dialogService;
            _keyDir = keyDir ?? DefaultKeyDir;
            Issue = new IssueViewModel(_keyDir, dialogService);
            Ledger = new LedgerViewModel(_keyDir, notesStore, dialogService);

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
                ? $"개인키 없음 — {_keyDir} 에 *.private.pem 이 있어야 발급 가능 (운영 절차 §1)"
                : $"서명 키: {string.Join(" · ", keys.Select(k => k.KeyId))}";

            (HasBackupWarning, BackupStatusText) = DescribeBackup(BackupService.GetStatus(_keyDir));

            Ledger.Refresh();
        }

        /// <summary>
        /// 배너의 백업 문구 · 경고 여부. 미반영 건수는 매체마다 보인다 — 예전엔 가장 최근 매체 하나로만 세어
        /// 두 번째 USB 를 갱신하지 않아도 '미반영 0건' 으로 보였다(VO-14). 대장을 모두 담은 매체가 2부 미만이면 경고.
        /// </summary>
        internal static (bool Warning, string Text) DescribeBackup(BackupService.BackupStatus status)
        {
            if (!status.HasRecord)
                return (true, "백업 기록 없음 — 오프라인 USB 2부에 [백업 실행] 필요 (운영 절차 §1a)");

            var text = $"마지막 백업 {status.NewestUtc![..10]} · 매체 {status.MediaCount}부 · 백업 미반영 발급 {status.DescribeMedia()}";
            if (status.NeedsBackup)
                text += status.MediaCount < BackupService.RequiredCopies
                    ? " — 다른 USB 에도 [백업 실행] 필요"
                    : " — 발급 주 말일까지 2부 갱신";
            return (status.NeedsBackup, text);
        }

        [RelayCommand]
        private void RunBackup()
        {
            var target = _dialogService.ShowSelectBackupFolderDialog();
            if (target == null) return;

            try
            {
                var result = BackupService.Run(_keyDir, target);
                // 등록 매체 수가 아니라 '대장을 모두 담은 매체' 가 2부인지로 안내한다 (VO-14)
                var status = BackupService.GetStatus(_keyDir);
                var notice = status.NeedsBackup
                    ? $"\n\n백업 규칙은 오프라인 매체 2부 — 다른 USB 에도 한 번 더 실행하세요.\n매체별 미반영: {status.DescribeMedia()}"
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
