using System.Diagnostics;
using System.IO;
using VMS.Camera.Configuration;

namespace VMS.Camera.Services
{
    /// <summary>
    /// 카메라 SDK 연동 진단 로그 — %LocalAppData%\BODA VISION AI\logs\camera.log.
    ///
    /// 배경: 노출/게인 등 SDK 파라미터 적용 실패가 Debug.WriteLine 으로만 남아
    /// Release 배포판(현장)에서는 흔적이 전혀 없었다 (v1.4.21 현장 보고 — Mech-Mind
    /// 노출 변경이 2D 이미지에 반영되지 않는데 원인 확인 불가). 이 로그는 현장에서
    /// 어느 SetValue 가 어떤 에러를 반환했는지 확정하기 위한 것이다.
    ///
    /// 실패해도 촬영 흐름을 막으면 안 되므로 IO 예외는 모두 삼킨다.
    /// </summary>
    public static class CameraLog
    {
        private const long MaxBytes = 1_000_000;   // 초과 시 .old 로 교체 — 현장 무한 증식 방지
        private static readonly object _lock = new();

        /// <summary>테스트 전용 — 로그 파일 경로 재지정.</summary>
        internal static string? PathOverrideForTests;

        private static string LogFile => PathOverrideForTests ?? AppDataPaths.GetPath("logs", "camera.log");

        public static void Write(string message)
        {
            Debug.WriteLine(message);
            try
            {
                lock (_lock)
                {
                    var path = LogFile;
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                        File.Move(path, path + ".old", overwrite: true);

                    File.AppendAllText(path,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // 로그 실패는 무시 — 진단 보조 수단이 본 기능을 방해하면 안 됨
            }
        }
    }
}
