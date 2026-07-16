using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VMS.Interfaces;

namespace VMS.Services
{
    public class ProcessService : IProcessService
    {
        // VMS 가 실행한 자식 프로세스(VisionSetup/AppSetup 등) 추적 — VMS 종료 시
        // 정리 대상. 참조를 버리면 잔존 프로세스를 정리할 방법이 없다 (현장 보고 2026-07-16).
        private readonly List<Process> _launched = new();
        private readonly object _lock = new();

        public void LaunchProcess(string fileName, string? arguments = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true
            };

            if (!string.IsNullOrEmpty(arguments))
            {
                startInfo.Arguments = arguments;
            }

            var process = Process.Start(startInfo);
            if (process != null)
            {
                lock (_lock)
                {
                    // 이미 종료된 항목은 이 기회에 정리 (목록 무한 증가 방지)
                    _launched.RemoveAll(p =>
                    {
                        try { return p.HasExited; }
                        catch { return true; }
                    });
                    _launched.Add(process);
                }
            }
        }

        public void ShutdownLaunchedProcesses(int gracefulTimeoutMs = 2000)
        {
            List<Process> snapshot;
            lock (_lock)
            {
                snapshot = _launched.ToList();
                _launched.Clear();
            }

            foreach (var p in snapshot)
            {
                try
                {
                    if (p.HasExited) continue;

                    // 메인 윈도우가 있으면 정상 닫기 요청만 — 미저장 작업(레시피 편집 등)이
                    // 있으면 자식 쪽 확인 다이얼로그가 뜨고 사용자가 결정한다 (강제 종료 금지).
                    // CloseMainWindow 가 false 면 윈도우 없는 잔존 프로세스 → 강제 종료.
                    if (p.CloseMainWindow())
                        p.WaitForExit(gracefulTimeoutMs);
                    else
                        p.Kill(entireProcessTree: true);
                }
                catch
                {
                    // 이미 종료됨 / 접근 불가 — 종료 경로에서 실패는 무시
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
    }
}
