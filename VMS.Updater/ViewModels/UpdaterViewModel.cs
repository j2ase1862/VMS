using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Updater.Services;

namespace VMS.Updater.ViewModels
{
    /// <summary>
    /// 업데이트 진행률 창의 ViewModel — MsiInstallEngine 진행 이벤트를 바인딩 속성으로 노출.
    /// 성공 시 msiexec 호환 종료 코드로 즉시 종료(부트스트랩이 이어서 VMS 재실행),
    /// 실패 시 오류 상태로 전환해 [닫기] 로 사용자가 확인 후 종료한다.
    /// </summary>
    public partial class UpdaterViewModel : ObservableObject
    {
        private const int ExitCodeSuccess = 0;
        private const int ExitCodeSuccessRebootRequired = 3010;

        private readonly MsiInstallEngine _engine;
        private int _exitCode;

        [ObservableProperty]
        private int _percent;

        [ObservableProperty]
        private string _statusText = "설치를 준비하는 중...";

        [ObservableProperty]
        private string _versionText = string.Empty;

        [ObservableProperty]
        private bool _failed;

        public UpdaterViewModel() : this(new MsiInstallEngine())
        {
        }

        internal UpdaterViewModel(MsiInstallEngine engine)
        {
            _engine = engine;
            _engine.ProgressChanged += p => RunOnUi(() => Percent = p);
            _engine.ActionChanged += a => RunOnUi(() => StatusText = a);
        }

        /// <summary>표시용 버전 문구 구성. 대상 버전은 MSI 파일명(VMS-x.y.z.msi)에서 유추.</summary>
        public void SetVersions(string? currentVersion, string msiPath)
        {
            var target = Regex.Match(Path.GetFileName(msiPath), @"(\d+\.\d+\.\d+(\.\d+)?)");
            VersionText = (currentVersion, target.Success) switch
            {
                (not null, true) => $"v{currentVersion}  →  v{target.Groups[1].Value}",
                (null, true) => $"v{target.Groups[1].Value}",
                (not null, false) => $"v{currentVersion} 에서 업데이트",
                _ => string.Empty,
            };
        }

        /// <summary>설치 실행. 성공하면 앱을 종료 코드와 함께 즉시 종료한다.</summary>
        public async Task RunAsync(string msiPath)
        {
            try
            {
                _exitCode = await Task.Run(() => _engine.Install(msiPath));
            }
            catch (Exception)
            {
                _exitCode = 1603; // ERROR_INSTALL_FAILURE — 부트스트랩이 실패로 판정하도록
            }

            if (_exitCode is ExitCodeSuccess or ExitCodeSuccessRebootRequired)
            {
                Percent = 100;
                StatusText = "설치 완료 — VMS를 다시 시작합니다.";
                // 완료 상태를 잠깐 보여주고 종료 (부트스트랩이 VMS 재실행을 이어받음).
                await Task.Delay(1200);
                Application.Current.Shutdown(_exitCode);
                return;
            }

            // 창이 이미 닫힌 상태(Alt+F4 후 백그라운드 설치)면 보여줄 곳이 없으니 즉시 종료.
            if (Application.Current.MainWindow?.IsVisible != true)
            {
                Application.Current.Shutdown(_exitCode);
                return;
            }

            Failed = true;
            StatusText = _exitCode switch
            {
                1602 => "설치가 취소되었습니다. 기존 버전이 유지됩니다.",
                1618 => "다른 설치가 진행 중입니다. 잠시 후 다시 시도하세요.",
                _ => $"설치에 실패했습니다 (코드 {_exitCode}). 기존 버전이 유지됩니다.",
            };
        }

        [RelayCommand]
        private void Close() => Application.Current.Shutdown(_exitCode);

        private static void RunOnUi(Action action)
            => Application.Current?.Dispatcher.BeginInvoke(action);
    }
}
