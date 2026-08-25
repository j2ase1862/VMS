using System.IO;
using System.Text;
using System.Text.Json;
using VMS.Core.Security.Licensing;

namespace VMS.AppSetup.Services
{
    /// <summary>
    /// "--import-license &lt;source&gt; &lt;result&gt;" 상승 모드의 본체 — UI 없이 라이선스
    /// 파일을 설치 경로로 복사한 뒤 결과 파일로 보고한다. WebServerConfigApplier 와
    /// 동일한 규약 (App.xaml.cs 가 일반 부팅 전에 분기, 호출측이 결과 파일 판정).
    ///
    /// 상승 컨텍스트에서 임의 파일이 설치 경로에 놓이지 않도록 복사 전에
    /// 서명 검증을 한 번 더 수행한다 (지문/만료는 호출측 UX 판단 — 여기선 서명만).
    /// </summary>
    internal static class LicenseImportApplier
    {
        public const string ArgName = "--import-license";

        internal sealed record Result(bool Ok, string? Error);

        public static int Run(string sourcePath, string resultPath)
        {
            Result result;
            try
            {
                result = Apply(sourcePath, LicenseFileStore.DefaultPath);
            }
            catch (Exception ex)
            {
                result = new Result(false, ex.Message);
            }
            return Report(result, resultPath);
        }

        internal static Result Apply(string sourcePath, string targetPath)
        {
            if (!File.Exists(sourcePath))
                return new Result(false, $"원본 파일이 없습니다: {sourcePath}");

            // 서명 위조/손상 파일만 차단 — 지문 불일치/만료는 Invalid 가 아니므로 통과 (호출측 UX 판단)
            var eval = LicenseValidator.Validate(
                File.ReadAllText(sourcePath), MachineFingerprint.GetCode(),
                DateOnly.FromDateTime(DateTime.Now));
            if (eval.Status == LicenseStatus.Invalid)
                return new Result(false, $"유효하지 않은 라이선스 파일: {eval.Message}");

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, overwrite: true);
            return new Result(true, null);
        }

        private static int Report(Result result, string resultPath)
        {
            try
            {
                File.WriteAllText(resultPath, JsonSerializer.Serialize(result), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 결과 파일조차 못 쓰면 exit code 로만 전달
            }
            return result.Ok ? 0 : 1;
        }
    }
}
