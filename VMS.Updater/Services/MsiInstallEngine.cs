using System.Runtime.InteropServices;
using System.Text;

namespace VMS.Updater.Services
{
    /// <summary>
    /// Windows Installer API(msi.dll)로 MSI 를 현재 프로세스에서 설치하고,
    /// 외부 UI 레코드 콜백으로 실제 진행률(0~100)과 현재 액션 설명을 보고한다.
    /// msiexec /passive 의 기본 진행률 바를 대체하는 브랜딩 UI 의 데이터 소스.
    ///
    /// 진행률 계산은 MSDN "Handling Progress Messages Using MsiSetExternalUI" 의
    /// 표준 알고리즘: Reset(전체 틱)/ProgressAddition(틱 추가)/ProgressReport(증가)/
    /// ActionInfo(ACTIONDATA 당 틱) 4종 메시지를 누적한다.
    /// </summary>
    public sealed class MsiInstallEngine
    {
        /// <summary>진행률 변경 (0~100). 스레드 풀 스레드에서 호출됨 — UI 는 Dispatcher 로 마샬링할 것.</summary>
        public event Action<int>? ProgressChanged;

        /// <summary>현재 설치 액션의 현지화된 설명 (예: "새 파일을 복사하는 중").</summary>
        public event Action<string>? ActionChanged;

        // 콜백 델리게이트는 네이티브에 전달되는 동안 GC 되지 않도록 필드로 유지.
        private readonly InstallUiHandlerRecord _handler;

        private long _total;
        private long _completed;
        private bool _forward = true;
        private bool _enableActionData;
        private long _actionDataStep;
        private int _lastPercent = -1;

        public MsiInstallEngine()
        {
            _handler = HandleMessage;
        }

        /// <summary>
        /// 설치를 동기 실행하고 msiexec 호환 종료 코드를 반환한다
        /// (0 성공, 3010 재부팅 필요 성공, 그 외 실패). 백그라운드 스레드에서 호출할 것.
        /// </summary>
        public int Install(string msiPath)
        {
            MsiSetInternalUI(InstallUiLevelNone, IntPtr.Zero);
            MsiSetExternalUIRecord(_handler, MessageFilter, IntPtr.Zero, out _);
            try
            {
                // REBOOT=ReallySuppress: 기존 부트스트랩의 /norestart 와 동일 정책.
                return (int)MsiInstallProduct(msiPath, "REBOOT=ReallySuppress");
            }
            finally
            {
                MsiSetExternalUIRecord(null, 0, IntPtr.Zero, out _);
            }
        }

        private int HandleMessage(IntPtr context, uint messageType, IntPtr record)
        {
            var kind = messageType & 0xFF000000;
            switch (kind)
            {
                case InstallMessageProgress:
                    return HandleProgress(record);

                case InstallMessageActionStart:
                    // 필드 2 = 액션의 현지화된 설명 (없으면 빈 문자열).
                    var description = GetRecordString(record, 2);
                    if (!string.IsNullOrWhiteSpace(description))
                        ActionChanged?.Invoke(description.Trim());
                    _enableActionData = false;
                    return IdOk;

                case InstallMessageActionData:
                    if (_enableActionData)
                    {
                        _completed += _forward ? _actionDataStep : -_actionDataStep;
                        ReportPercent();
                    }
                    return IdOk;

                default:
                    return 0; // 처리 안 함 — 설치기가 기본 동작 수행
            }
        }

        private int HandleProgress(IntPtr record)
        {
            if (record == IntPtr.Zero) return IdOk;

            switch (MsiRecordGetInteger(record, 1))
            {
                case 0: // Reset — 새 진행 단계 시작
                    _total = Math.Max(0, MsiRecordGetInteger(record, 2));
                    _forward = MsiRecordGetInteger(record, 3) == 0;
                    _completed = 0;
                    _enableActionData = false;
                    ReportPercent();
                    break;

                case 1: // ActionInfo — ACTIONDATA 메시지당 틱 수
                    if (MsiRecordGetInteger(record, 3) != 0)
                    {
                        _enableActionData = true;
                        _actionDataStep = Math.Max(0, MsiRecordGetInteger(record, 2));
                    }
                    else
                    {
                        _enableActionData = false;
                    }
                    break;

                case 2: // ProgressReport — 명시적 증가
                    if (_total > 0)
                    {
                        var tick = MsiRecordGetInteger(record, 2);
                        _completed += _forward ? tick : -tick;
                        ReportPercent();
                    }
                    break;

                case 3: // ProgressAddition — 전체 틱 확장
                    _total += Math.Max(0, MsiRecordGetInteger(record, 2));
                    break;
            }

            return IdOk;
        }

        private void ReportPercent()
        {
            if (_total <= 0) return;
            var percent = (int)Math.Clamp(_completed * 100 / _total, 0, 100);
            if (percent == _lastPercent) return;
            _lastPercent = percent;
            ProgressChanged?.Invoke(percent);
        }

        private static string GetRecordString(IntPtr record, uint field)
        {
            uint length = 0;
            // 길이 조회 (버퍼 0) → 실제 조회. ERROR_MORE_DATA(234) 가 정상 경로.
            MsiRecordGetString(record, field, new StringBuilder(0), ref length);
            if (length == 0) return string.Empty;
            length++; // null 종료 문자
            var buffer = new StringBuilder((int)length);
            return MsiRecordGetString(record, field, buffer, ref length) == 0
                ? buffer.ToString()
                : string.Empty;
        }

        #region Native (msi.dll)

        private const int IdOk = 1;
        private const uint InstallUiLevelNone = 2;

        private const uint InstallMessageProgress = 0x0A000000;
        private const uint InstallMessageActionStart = 0x08000000;
        private const uint InstallMessageActionData = 0x09000000;

        // INSTALLLOGMODE: PROGRESS | ACTIONSTART | ACTIONDATA
        private const uint MessageFilter = 0x400 | 0x100 | 0x200;

        private delegate int InstallUiHandlerRecord(IntPtr context, uint messageType, IntPtr record);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiInstallProduct(string packagePath, string commandLine);

        [DllImport("msi.dll")]
        private static extern int MsiSetInternalUI(uint uiLevel, IntPtr window);

        [DllImport("msi.dll")]
        private static extern uint MsiSetExternalUIRecord(
            InstallUiHandlerRecord? handler, uint messageFilter, IntPtr context, out IntPtr previousHandler);

        [DllImport("msi.dll")]
        private static extern int MsiRecordGetInteger(IntPtr record, uint field);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiRecordGetString(
            IntPtr record, uint field, StringBuilder value, ref uint valueLength);

        #endregion
    }
}
