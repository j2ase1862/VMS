using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using VMS.AppSetup.Interfaces;

namespace VMS.AppSetup.Services
{
    /// <summary>
    /// MSI 동봉 로컬 Web 서버(BODA.VMS.Web) 초기 구성 서비스.
    /// 상태 조회·검증·시크릿 생성은 현재 권한으로 수행하고, Program Files 쓰기 +
    /// 서비스 제어가 필요한 적용 단계만 AppSetup 자신을 UAC 상승 재실행
    /// (<see cref="WebServerConfigApplier"/>) 으로 위임한다.
    /// </summary>
    public class WebServerSetupService : IWebServerSetupService
    {
        // MSI(Package.wxs WebSvcInstall) / 오프라인 스크립트 / 운영 런북과 동일한 이름·포트
        internal const string ServiceName = "BodaVmsWeb";
        internal const int Port = 5292;
        internal const string ExeName = "BODA.VMS.Web.exe";
        internal const string ConfigFileName = "appsettings.Production.json";

        private readonly string _webDir;
        private readonly string _dataDir;
        private readonly string _serviceName;

        public WebServerSetupService()
            : this(Path.Combine(AppContext.BaseDirectory, "Web"),
                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BODA", "VMS"),
                   ServiceName)
        {
        }

        // 테스트 전용 — 임시 폴더로 상태 판정·구성 생성 로직을 검증 (InternalsVisibleTo)
        internal WebServerSetupService(string webDir, string dataDir, string serviceName)
        {
            _webDir = webDir;
            _dataDir = dataDir;
            _serviceName = serviceName;
        }

        public WebServerStatus GetStatus()
        {
            if (!File.Exists(Path.Combine(_webDir, ExeName)))
                return new WebServerStatus(WebServerInstallState.NotInstalled, null, _webDir);

            var state = File.Exists(Path.Combine(_webDir, ConfigFileName))
                ? WebServerInstallState.Configured
                : WebServerInstallState.NotConfigured;

            return new WebServerStatus(state, QueryServiceStatus(), _webDir);
        }

        private string? QueryServiceStatus()
        {
            try
            {
                using var sc = new ServiceController(_serviceName);
                return sc.Status.ToString(); // 미등록 서비스는 Status 접근 시 throw
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>시작 유형이 자동인지 (Automatic 은 delayed-auto 포함 — SCM 이 구분값을 노출하지 않음).</summary>
        private bool IsAutoStartType()
        {
            try
            {
                using var sc = new ServiceController(_serviceName);
                return sc.StartType == ServiceStartMode.Automatic;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public async Task<WebServerConfigureResult> ConfigureAsync(string adminPassword)
        {
            var status = GetStatus();
            if (status.State == WebServerInstallState.NotInstalled)
                return new WebServerConfigureResult(false, "이 PC 에 Web 서버가 설치되어 있지 않습니다.");
            if (status.State == WebServerInstallState.Configured)
                return new WebServerConfigureResult(false, "이미 구성되어 있습니다. 비밀번호 재설정은 Reset-AdminPassword 도구를 사용하세요.");
            if (adminPassword.Length < 8)
                return new WebServerConfigureResult(false, "admin 비밀번호는 8자 이상이어야 합니다 (12자 이상 권장).");

            var payload = new WebServerConfigApplier.Payload(
                WebDir: _webDir,
                DataDir: _dataDir,
                ServiceName: _serviceName,
                ConfigJson: BuildConfigJson(adminPassword, GenerateJwtKey(), Path.Combine(_dataDir, "BodaVision.db")));

            // 비밀번호가 명령줄(프로세스 목록에 노출)에 실리지 않도록 사용자 프로필 임시 파일로
            // 전달하고 적용 직후 삭제. 최종 산출물(appsettings.Production.json)의 시크릿 보관은
            // 오프라인 설치 스크립트와 동일한 운영 방식이다.
            var stamp = Guid.NewGuid().ToString("N");
            var payloadPath = Path.Combine(Path.GetTempPath(), $"boda-web-cfg-{stamp}.json");
            var resultPath = Path.Combine(Path.GetTempPath(), $"boda-web-cfg-{stamp}.result.json");
            try
            {
                await File.WriteAllTextAsync(payloadPath,
                    JsonSerializer.Serialize(payload), new UTF8Encoding(false));

                return await RunElevatedAsync(resultPath, WebServerConfigApplier.ArgName, payloadPath);
            }
            finally
            {
                TryDelete(payloadPath);
                TryDelete(resultPath);
            }
        }

        public async Task<WebServerConfigureResult> StartServiceAsync()
        {
            var status = GetStatus();
            if (status.State == WebServerInstallState.NotInstalled)
                return new WebServerConfigureResult(false, "이 PC 에 Web 서버가 설치되어 있지 않습니다.");
            if (status.State == WebServerInstallState.NotConfigured)
                return new WebServerConfigureResult(false, "초기 구성이 먼저 필요합니다 — 위의 [초기 구성 실행]을 사용하세요.");
            if (status.ServiceStatus == null)
                return new WebServerConfigureResult(false, "서비스가 등록되어 있지 않습니다 — MSI 재설치(복구)가 필요합니다.");
            // 실행 중이어도 시작 유형이 수동(demand)이면 자동 전환까지 수행 — 수동 MSI 업그레이드가
            // 서비스를 demand 로 재등록한 뒤 여기서 조기 성공 반환하면 다음 부팅에 서비스가 안 뜬다
            if (status.ServiceStatus == nameof(System.ServiceProcess.ServiceControllerStatus.Running)
                && IsAutoStartType())
                return new WebServerConfigureResult(true, null);

            var resultPath = Path.Combine(Path.GetTempPath(),
                $"boda-web-start-{Guid.NewGuid():N}.result.json");
            try
            {
                return await RunElevatedAsync(resultPath, WebServerConfigApplier.StartArgName, _serviceName);
            }
            finally
            {
                TryDelete(resultPath);
            }
        }

        /// <summary>
        /// AppSetup 자신을 UAC 상승으로 재실행해 상승 모드(<paramref name="args"/> + 결과 파일 경로)를
        /// 수행하고 결과 파일로 성공 여부를 판정한다.
        /// </summary>
        private static async Task<WebServerConfigureResult> RunElevatedAsync(string resultPath, params string[] args)
        {
            try
            {
                var exePath = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, "VMS.AppSetup.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas", // UAC 상승 — 거부 시 Win32Exception(1223)
                };
                foreach (var arg in args)
                    psi.ArgumentList.Add(arg);
                psi.ArgumentList.Add(resultPath);

                using var proc = Process.Start(psi);
                if (proc == null)
                    return new WebServerConfigureResult(false, "적용 프로세스를 시작하지 못했습니다.");
                await proc.WaitForExitAsync();

                if (File.Exists(resultPath))
                {
                    var result = JsonSerializer.Deserialize<WebServerConfigApplier.Result>(
                        await File.ReadAllTextAsync(resultPath));
                    if (result != null)
                        return new WebServerConfigureResult(result.Ok, result.Error);
                }
                return new WebServerConfigureResult(false, $"적용 결과를 확인하지 못했습니다 (exit={proc.ExitCode}).");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new WebServerConfigureResult(false, "관리자 권한 승인이 거부되어 적용하지 못했습니다.");
            }
        }

        public async Task<bool> SmokeTestAsync()
        {
            // 첫 부팅은 DB 마이그레이션을 수행하므로 잠시 걸릴 수 있다 — 2초 간격 최대 24초 재시도
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    var resp = await http.GetAsync($"http://localhost:{Port}/health");
                    if (resp.IsSuccessStatusCode) return true;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
            return false;
        }

        /// <summary>암호학적 무작위 64바이트 → Base64 88자 (앱 요구: 32자 이상).</summary>
        internal static string GenerateJwtKey()
        {
            var bytes = new byte[64];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// 오프라인 설치 스크립트(Install-Web-Offline.ps1)와 동일한 형태의 운영 설정.
        /// Initial:AdminPassword 는 첫 부팅 admin 시드에만 쓰이고 이후 무시된다.
        /// </summary>
        internal static string BuildConfigJson(string adminPassword, string jwtKey, string dbPath)
        {
            var config = new Dictionary<string, object>
            {
                ["Jwt"] = new Dictionary<string, string> { ["Key"] = jwtKey },
                ["Initial"] = new Dictionary<string, string> { ["AdminPassword"] = adminPassword },
                ["ConnectionStrings"] = new Dictionary<string, string> { ["DefaultConnection"] = $"Data Source={dbPath}" },
            };
            return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
