using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Boda.LicGen.App.Messages;
using Boda.LicGen.App.Services;
using VMS.Core.Security.Licensing;

namespace Boda.LicGen.App.ViewModels
{
    public sealed record SigningKey(string KeyId, string KeyPath);

    /// <summary>발급 탭 — 입력 검증·서명은 LicenseIssuer(CLI 공용) 가 수행.</summary>
    public partial class IssueViewModel : ObservableObject
    {
        private readonly string _keyDir;
        private readonly IDialogService _dialogService;

        public IReadOnlyList<LicenseKind> Kinds { get; } =
            new[] { LicenseKind.Production, LicenseKind.Trial, LicenseKind.Internal };

        public ObservableCollection<SigningKey> AvailableKeys { get; } = new();

        [ObservableProperty]
        private LicenseKind _selectedKind = LicenseKind.Production;

        [ObservableProperty]
        private SigningKey? _selectedKey;

        [ObservableProperty]
        private string _customer = "";

        [ObservableProperty]
        private string _fingerprint = "";

        [ObservableProperty]
        private string _seatsText = "1";

        [ObservableProperty]
        private DateTime? _maintenanceUntil = DateTime.Today.AddYears(1);

        [ObservableProperty]
        private DateTime? _expiresAt;

        public IssueViewModel(string keyDir, IDialogService dialogService)
        {
            _keyDir = keyDir;
            _dialogService = dialogService;
            ReloadAvailableKeys();
        }

        /// <summary>키 폴더의 *.private.pem 을 다시 스캔 — 배너와 콤보가 함께 쓴다.</summary>
        public IReadOnlyList<SigningKey> ReloadAvailableKeys()
        {
            var keys = Directory.Exists(_keyDir)
                ? Directory.GetFiles(_keyDir, "*.private.pem")
                    .Select(p => new SigningKey(
                        Path.GetFileName(p).Replace(".private.pem", "", StringComparison.OrdinalIgnoreCase), p))
                    .OrderBy(k => k.KeyId, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<SigningKey>();

            AvailableKeys.Clear();
            foreach (var key in keys) AvailableKeys.Add(key);
            SelectedKey = PreferredKeyFor(SelectedKind);
            return keys;
        }

        // 용도 분리(운영 절차 §1): 고객 발급(Production/Trial)=prod-*, 사내(Internal)=dev-*
        partial void OnSelectedKindChanged(LicenseKind value) => SelectedKey = PreferredKeyFor(value);

        private SigningKey? PreferredKeyFor(LicenseKind kind)
        {
            var prefix = kind == LicenseKind.Internal ? "dev-" : "prod-";
            return AvailableKeys.FirstOrDefault(k => k.KeyId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                   ?? AvailableKeys.FirstOrDefault();
        }

        [RelayCommand]
        private void IssueLicense()
        {
            if (SelectedKey == null)
            {
                _dialogService.ShowError($"서명 키 없음 — {_keyDir} 에 개인키(*.private.pem)가 필요합니다.", "발급 불가");
                return;
            }
            if (!int.TryParse(SeatsText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seats))
            {
                _dialogService.ShowError("좌석 수는 숫자로 입력하세요.", "입력 오류");
                return;
            }

            var request = new IssueRequest(
                KeyPath: SelectedKey.KeyPath,
                KeyId: SelectedKey.KeyId,
                Customer: Customer.Trim(),
                Fingerprint: Fingerprint.Trim(),
                Kind: SelectedKind,
                MaxClients: seats,
                MaintenanceUntil: MaintenanceUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ExpiresAt: ExpiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            var today = DateOnly.FromDateTime(DateTime.Now);
            try
            {
                // 대장 기록 전에 규칙 위반을 걸러낸다 — 저장 취소로도 대장이 오염되지 않게
                LicenseIssuer.ValidateRules(request, today);
            }
            catch (ArgumentException ex)
            {
                _dialogService.ShowError(ex.Message, "발급 규칙 위반");
                return;
            }

            var summary =
                $"고객명: {request.Customer}\n종류: {request.Kind} (서명 키 {request.KeyId})\n" +
                $"지문: {request.Fingerprint}\n좌석: {seats}\n" +
                $"유지보수 만료: {request.MaintenanceUntil ?? "-"}\n실행 만료: {request.ExpiresAt ?? "- (영구)"}\n\n" +
                "발급 즉시 대장(ledger.jsonl)에 기록됩니다. 진행할까요?";
            if (!_dialogService.ShowConfirmation(summary, "발급 확인")) return;

            var savePath = _dialogService.ShowSaveLicenseDialog(LicenseFileStore.FileName);
            if (savePath == null) return;

            try
            {
                var result = LicenseIssuer.Issue(request, today);
                File.WriteAllText(savePath, result.LicenseJson);

                WeakReferenceMessenger.Default.Send(new LicenseIssuedMessage(result.LicenseId));
                _dialogService.ShowInformation(
                    $"{result.LicenseId} 발급 완료\n\n파일: {savePath}\n대장: {result.LedgerPath}\n\n" +
                    "대상 PC 의 AppSetup [라이선스 파일 가져오기] 로 설치하세요.",
                    "발급 완료");

                Customer = "";
                Fingerprint = "";
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or FormatException)
            {
                _dialogService.ShowError(ex.Message, "발급 실패");
            }
        }
    }
}
