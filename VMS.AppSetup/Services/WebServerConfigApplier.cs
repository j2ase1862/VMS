using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;

namespace VMS.AppSetup.Services
{
    /// <summary>
    /// "--apply-web-config &lt;payload&gt; &lt;result&gt;" / "--start-web-service &lt;서비스명&gt; &lt;result&gt;"
    /// 상승 모드의 본체 — UI 없이 Web 서버 운영 설정 작성·서비스 auto 전환 + 시작을 수행한 뒤
    /// 결과 파일로 보고한다. App.xaml.cs 가 일반 부팅 전에 이 모드를 분기하며,
    /// 호출측(WebServerSetupService)은 결과 파일을 읽어 성공 여부를 판정한다.
    /// </summary>
    internal static class WebServerConfigApplier
    {
        public const string ArgName = "--apply-web-config";
        public const string StartArgName = "--start-web-service";

        internal sealed record Payload(string WebDir, string DataDir, string ServiceName, string ConfigJson);
        internal sealed record Result(bool Ok, string? Error);

        public static int Run(string payloadPath, string resultPath)
        {
            Result result;
            try
            {
                var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(payloadPath))
                    ?? throw new InvalidOperationException("payload 파싱 실패");
                result = Apply(payload);
            }
            catch (Exception ex)
            {
                result = new Result(false, ex.Message);
            }
            return Report(result, resultPath);
        }

        /// <summary>구성 완료 후 서비스만 내려간 경우(마이그레이션 §13.4 / 수동 정지)의 재시작 모드.</summary>
        public static int RunStart(string serviceName, string resultPath)
        {
            Result result;
            try
            {
                result = EnsureAutoStartAndRun(serviceName);
            }
            catch (Exception ex)
            {
                result = new Result(false, ex.Message);
            }
            return Report(result, resultPath);
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

        internal static Result Apply(Payload payload)
        {
            var configPath = Path.Combine(payload.WebDir, WebServerSetupService.ConfigFileName);
            if (!File.Exists(Path.Combine(payload.WebDir, WebServerSetupService.ExeName)))
                return new Result(false, $"Web 서버 실행 파일이 없습니다: {payload.WebDir}");
            // 재구성으로 기존 운영 시크릿(Jwt:Key)이 덮어써지면 발급된 토큰이 전부 무효화된다 — 거부
            if (File.Exists(configPath))
                return new Result(false, "운영 설정이 이미 존재합니다 — 덮어쓰지 않습니다.");

            Directory.CreateDirectory(payload.DataDir);
            // BOM 없는 UTF-8 (ASP.NET Core 설정 로더 호환)
            File.WriteAllText(configPath, payload.ConfigJson, new UTF8Encoding(false));

            return EnsureAutoStartAndRun(payload.ServiceName);
        }

        /// <summary>
        /// 서비스 부팅 자동시작 전환 + 시작 (관리자 권한 필요). MSI 는 크래시 루프 방지를 위해
        /// demand 로 등록하므로, 구성 시점과 수동 재시작 시점 모두 자동 전환을 함께 보장한다.
        /// 시작 유형은 delayed-auto — 일반 auto 는 부팅 직후 디스크·백신 경합으로 30초 SCM
        /// 타임아웃(이벤트 7009)에 걸려 서비스가 안 뜨는 사례가 있었다 (세연공장 2026-08-27).
        /// </summary>
        internal static Result EnsureAutoStartAndRun(string serviceName)
        {
            var sc = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config {serviceName} start= delayed-auto",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            sc?.WaitForExit();
            if (sc == null || sc.ExitCode != 0)
                return new Result(false, $"서비스 자동시작 전환 실패 (sc config exit={sc?.ExitCode})");

            using var controller = new ServiceController(serviceName);
            if (controller.Status != ServiceControllerStatus.Running)
            {
                controller.Start();
                // 첫 부팅은 DB 마이그레이션 포함 — 넉넉히 대기
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(40));
            }
            return new Result(true, null);
        }
    }
}
