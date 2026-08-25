using System;
using System.IO;

namespace VMS.Core.Security.Licensing
{
    /// <summary>
    /// license.lic 위치 규약 + 로드 — spec §3.
    ///
    /// 단일 경로 C:\ProgramData\BODA\VMS\license.lic — VMS(사용자 세션)와
    /// BODA.VMS.Web(서비스 계정)이 같은 파일을 읽는다. %LocalAppData% 는 서비스
    /// 계정이 못 읽으므로 금지. 쓰기(가져오기)는 AppSetup UAC 상승 경로 담당.
    /// </summary>
    public static class LicenseFileStore
    {
        public const string FileName = "license.lic";

        /// <summary>C:\ProgramData\BODA\VMS — Web 서버 데이터 디렉토리와 동일.</summary>
        public static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BODA", "VMS");

        public static string DefaultPath => Path.Combine(DefaultDirectory, FileName);

        /// <summary>
        /// 파일 텍스트 로드. 부재 시 null, 읽기 오류는 오류 메시지 반환 —
        /// 호출자가 Missing(부재)과 Invalid(읽기 실패)를 구분할 수 있게.
        /// </summary>
        public static (string? json, string? error) TryRead(string? path = null)
        {
            var target = path ?? DefaultPath;
            try
            {
                if (!File.Exists(target)) return (null, null);
                return (File.ReadAllText(target), null);
            }
            catch (Exception ex)
            {
                return (null, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
