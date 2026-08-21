using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models.Updates;
using VMS.Core.Security;

namespace VMS.Core.Services
{
    /// <summary>
    /// IUpdateInstallService 구현 — GitHub Releases 의 MSI 자산을 임시 폴더로 스트리밍
    /// 다운로드(SHA-256 검증)하고, 상승(UAC) PowerShell 부트스트랩 스크립트로 설치를 위임한다.
    ///
    /// 부트스트랩 스크립트가 하는 일 (관리자 권한, 앱 종료 후):
    ///   1. VMS 프로세스 종료 대기 (files-in-use 방지)
    ///   2. BodaVmsWeb 서비스 사전 상태 기록 (MajorUpgrade 가 서비스를 demand 로 재등록하므로)
    ///   3. VMS.Updater(브랜딩 진행률 창)를 임시 폴더로 복사해 설치 실행
    ///      — 업데이터가 없거나 복사 실패 시 msiexec /i /passive /norestart 폴백
    ///   4. 서비스가 이전에 auto/실행 중이었으면 auto 전환 + 시작 복원
    ///      (AppSetup 수동 시작 단계 자동화 — WebServerConfigApplier.EnsureAutoStartAndRun 과 동일 정책)
    ///   5. VMS 재실행 (explorer 경유 — 상승 권한 미상속)
    ///   6. 성공 시 MSI 삭제, 전 과정 로그를 ProgramData\BODA\VMS\update-bootstrap.log 에 기록
    /// </summary>
    public sealed class UpdateInstallService : IUpdateInstallService
    {
        // VMS.MasterSetup/Package.wxs WebSvcInstall · AppSetup WebServerSetupService 와 동일 이름.
        internal const string WebServiceName = "BodaVmsWeb";

        private const int BufferSize = 81920;
        private const long ProgressReportChunk = 2 * 1024 * 1024; // 2 MB 마다 진행률 보고
        private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);

        private readonly string _downloadDir;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public UpdateInstallService()
            : this(httpClient: null, downloadDir: null)
        {
        }

        /// <summary>테스트 친화 ctor — HttpClient / 다운로드 폴더 주입 가능.</summary>
        public UpdateInstallService(HttpClient? httpClient, string? downloadDir)
        {
            // MSI 는 1 GB 이상 — 전체 요청 타임아웃 대신 호출 측 CancellationToken 으로 제어.
            // 스트리밍(ResponseHeadersRead) 이라 MaxResponseContentBufferSize 는 적용되지 않는다.
            _httpClient = httpClient ?? HttpClientPolicy.Build(Timeout.InfiniteTimeSpan);
            _downloadDir = downloadDir
                ?? Path.Combine(Path.GetTempPath(), "BODA-VMS-Update");
        }

        public async Task<UpdateDownloadResult> DownloadAsync(
            UpdateInfo info,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (_disposed) return UpdateDownloadResult.Failure("서비스가 이미 해제되었습니다.");
            if (string.IsNullOrWhiteSpace(info.DownloadUrl))
                return UpdateDownloadResult.Failure("릴리스에 MSI 파일이 없습니다.");

            var fileName = !string.IsNullOrWhiteSpace(info.DownloadFileName)
                ? info.DownloadFileName
                : "BODA-VMS-Update.msi";
            var targetPath = Path.Combine(_downloadDir, fileName);
            var partialPath = targetPath + ".partial";

            try
            {
                Directory.CreateDirectory(_downloadDir);

                // 이전 시도에서 이미 받아 검증까지 끝난 파일이면 재사용 (1 GB+ 재다운로드 방지).
                if (await IsExistingFileValidAsync(targetPath, info, cancellationToken).ConfigureAwait(false))
                {
                    progress?.Report(new UpdateDownloadProgress(info.DownloadSizeBytes, info.DownloadSizeBytes));
                    return UpdateDownloadResult.Success(targetPath);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);

                // 헤더 수신까지만 별도 타임아웃 — 본문 스트리밍은 취소 토큰으로만 제어.
                using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                headerCts.CancelAfter(HeaderTimeout);
                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerCts.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return UpdateDownloadResult.Failure($"다운로드 실패 (HTTP {(int)response.StatusCode}).");

                var totalBytes = info.DownloadSizeBytes > 0
                    ? info.DownloadSizeBytes
                    : response.Content.Headers.ContentLength ?? 0;

                string actualSha256;
                long received = 0;
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    await using var source = await response.Content
                        .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using (var target = new FileStream(
                        partialPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                    {
                        var buffer = new byte[BufferSize];
                        long lastReported = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                            hash.AppendData(buffer, 0, read);
                            received += read;

                            if (received - lastReported >= ProgressReportChunk)
                            {
                                lastReported = received;
                                progress?.Report(new UpdateDownloadProgress(received, totalBytes));
                            }
                        }
                    }
                    actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                }

                if (info.DownloadSizeBytes > 0 && received != info.DownloadSizeBytes)
                {
                    TryDelete(partialPath);
                    return UpdateDownloadResult.Failure(
                        $"다운로드 크기가 일치하지 않습니다 (기대 {info.DownloadSizeBytes:N0}, 실제 {received:N0} 바이트).");
                }

                if (!string.IsNullOrEmpty(info.DownloadSha256) &&
                    !string.Equals(actualSha256, info.DownloadSha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(partialPath);
                    return UpdateDownloadResult.Failure(
                        "다운로드 파일의 SHA-256 이 릴리스 정보와 일치하지 않습니다. 파일이 손상되었거나 변조되었을 수 있습니다.");
                }

                TryDelete(targetPath);
                File.Move(partialPath, targetPath);

                progress?.Report(new UpdateDownloadProgress(received, totalBytes));
                return UpdateDownloadResult.Success(targetPath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDelete(partialPath);
                throw; // 사용자 취소 — 호출 측이 구분 처리
            }
            catch (Exception ex)
            {
                TryDelete(partialPath);
                Debug.WriteLine($"[UpdateInstall] {ex.GetType().Name}: {ex.Message}");
                return UpdateDownloadResult.Failure($"다운로드 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        public UpdateInstallLaunchResult LaunchInstaller(string msiPath)
        {
            if (!File.Exists(msiPath))
                return UpdateInstallLaunchResult.Failure($"MSI 파일을 찾을 수 없습니다: {msiPath}");

            try
            {
                var vmsExePath = Environment.ProcessPath ?? string.Empty;
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "BODA", "VMS", "update-bootstrap.log");

                var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version is { } v
                    ? $"{v.Major}.{v.Minor}.{v.Build}"
                    : null;
                var script = BuildBootstrapScript(
                    msiPath, vmsExePath, Environment.ProcessId, logPath, currentVersion);
                var scriptPath = Path.Combine(_downloadDir, "update-bootstrap.ps1");
                Directory.CreateDirectory(_downloadDir);
                // BOM 있는 UTF-8 — Windows PowerShell 5.1 이 BOM 없으면 ANSI 로 해석.
                File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = true,
                    Verb = "runas", // UAC 상승 — 거부 시 Win32Exception(1223)
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-WindowStyle");
                psi.ArgumentList.Add("Hidden");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                psi.ArgumentList.Add("-File");
                psi.ArgumentList.Add(scriptPath);

                var proc = Process.Start(psi);
                return proc != null
                    ? UpdateInstallLaunchResult.Success()
                    : UpdateInstallLaunchResult.Failure("설치 프로세스를 시작하지 못했습니다.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return UpdateInstallLaunchResult.Declined();
            }
            catch (Exception ex)
            {
                return UpdateInstallLaunchResult.Failure($"설치 시작 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        /// <summary>
        /// 상승 모드에서 실행될 부트스트랩 스크립트 본문. Windows PowerShell 5.1 호환 문법만 사용.
        /// 로그 메시지는 인코딩 문제를 피하려 영문 고정.
        /// </summary>
        internal static string BuildBootstrapScript(
            string msiPath, string vmsExePath, int vmsPid, string logPath, string? currentVersion = null)
        {
            static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Continue'");
            sb.AppendLine($"$LogPath = {Quote(logPath)}");
            sb.AppendLine("function Write-Log([string]$m) {");
            sb.AppendLine("  try { Add-Content -Path $LogPath -Value (\"{0} {1}\" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m) -Encoding UTF8 } catch {}");
            sb.AppendLine("}");
            sb.AppendLine("try { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null } catch {}");
            sb.AppendLine("Write-Log '=== update bootstrap start ==='");

            // 1) VMS 종료 대기 — msiexec files-in-use 방지
            sb.AppendLine($"$vmsExe = {Quote(vmsExePath)}");
            sb.AppendLine($"$vmsPid = {vmsPid}");
            sb.AppendLine("$p = Get-Process -Id $vmsPid -ErrorAction SilentlyContinue");
            sb.AppendLine("if ($p) { Write-Log (\"waiting for VMS exit (pid={0})\" -f $vmsPid); $p.WaitForExit(60000) | Out-Null }");

            // 2) Web 서비스 사전 상태 — MajorUpgrade 가 서비스를 삭제 후 demand 로 재등록하므로
            //    이전 상태(auto/실행 중)를 여기서 기억해 두었다가 설치 후 복원한다.
            sb.AppendLine($"$svcName = {Quote(WebServiceName)}");
            sb.AppendLine("$svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue");
            sb.AppendLine("$wasRunning = ($null -ne $svc) -and ($svc.Status -eq 'Running')");
            sb.AppendLine("$wasAuto = ($null -ne $svc) -and ($svc.StartType -eq 'Automatic')");
            sb.AppendLine("Write-Log (\"web service pre-state: present={0} running={1} auto={2}\" -f ($null -ne $svc), $wasRunning, $wasAuto)");

            // 3) MSI 설치 — 브랜딩 진행률 창(VMS.Updater) 우선, 없으면 msiexec /passive 폴백.
            //    업데이터는 자신도 MSI 로 교체되는 파일이므로 설치 폴더에서 직접 실행하지 않고
            //    MSI 옆 임시 폴더로 복사해 실행한다 (files-in-use 방지).
            //    복사 목록은 VMS.Updater.csproj 의 패키지 정책과 짝: VMS.Updater.* + CommunityToolkit.Mvvm.dll.
            sb.AppendLine($"$msi = {Quote(msiPath)}");
            sb.AppendLine("$updaterExe = ''");
            sb.AppendLine("if ($vmsExe -ne '') {");
            sb.AppendLine("  $vmsDir = Split-Path -Parent $vmsExe");
            sb.AppendLine("  if (Test-Path -LiteralPath (Join-Path $vmsDir 'VMS.Updater.exe')) {");
            sb.AppendLine("    $updDir = Join-Path (Split-Path -Parent $msi) 'updater'");
            sb.AppendLine("    try {");
            sb.AppendLine("      New-Item -ItemType Directory -Force -Path $updDir | Out-Null");
            sb.AppendLine("      Copy-Item -Path (Join-Path $vmsDir 'VMS.Updater.*') -Destination $updDir -Force -ErrorAction Stop");
            sb.AppendLine("      Copy-Item -Path (Join-Path $vmsDir 'CommunityToolkit.Mvvm.dll') -Destination $updDir -Force -ErrorAction Stop");
            sb.AppendLine("      $updaterExe = Join-Path $updDir 'VMS.Updater.exe'");
            sb.AppendLine("    } catch { Write-Log (\"updater staging FAILED, fallback to msiexec: {0}\" -f $_.Exception.Message); $updaterExe = '' }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("if (($updaterExe -ne '') -and (Test-Path -LiteralPath $updaterExe)) {");
            sb.AppendLine("  Write-Log (\"installing via updater UI: {0}\" -f $msi)");
            var updaterArgs = currentVersion is null
                ? "('\"{0}\"' -f $msi)"
                : $"('\"{{0}}\"' -f $msi), '--current', {Quote(currentVersion)}";
            sb.AppendLine($"  $proc = Start-Process -FilePath $updaterExe -ArgumentList {updaterArgs} -Wait -PassThru");
            sb.AppendLine("} else {");
            sb.AppendLine("  Write-Log (\"installing via msiexec: {0}\" -f $msi)");
            sb.AppendLine("  $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList '/i', ('\"{0}\"' -f $msi), '/passive', '/norestart' -Wait -PassThru");
            sb.AppendLine("}");
            sb.AppendLine("$code = $proc.ExitCode");
            sb.AppendLine("Write-Log (\"install exit code: {0}\" -f $code)");
            sb.AppendLine("$installOk = ($code -eq 0) -or ($code -eq 3010)"); // 3010 = 재부팅 필요하나 성공

            // 4) Web 서비스 상태 복원 — AppSetup 수동 [서비스 시작] 단계 자동화
            sb.AppendLine("if ($installOk -and ($wasAuto -or $wasRunning)) {");
            sb.AppendLine("  if ($wasAuto) { & sc.exe config $svcName start= auto | Out-Null; Write-Log 'web service start type restored to auto' }");
            sb.AppendLine("  try {");
            sb.AppendLine("    Start-Service -Name $svcName -ErrorAction Stop");
            sb.AppendLine("    Write-Log 'web service started'");
            sb.AppendLine("  } catch { Write-Log (\"web service start FAILED: {0}\" -f $_.Exception.Message) }");
            sb.AppendLine("}");
            sb.AppendLine("if (-not $installOk) { Write-Log 'install FAILED - previous version remains' }");

            // 5) VMS 재실행 — explorer 경유로 상승 권한을 상속시키지 않음
            sb.AppendLine("if (($vmsExe -ne '') -and (Test-Path -LiteralPath $vmsExe)) {");
            sb.AppendLine("  Start-Process -FilePath 'explorer.exe' -ArgumentList ('\"{0}\"' -f $vmsExe)");
            sb.AppendLine("  Write-Log (\"relaunched: {0}\" -f $vmsExe)");
            sb.AppendLine("} else { Write-Log (\"VMS exe not found: {0}\" -f $vmsExe) }");

            // 6) 성공 시 MSI + 업데이터 임시 사본 정리 (1 GB+ 임시 파일 방치 방지)
            sb.AppendLine("if ($installOk) { Remove-Item -LiteralPath $msi -Force -ErrorAction SilentlyContinue; Write-Log 'msi removed' }");
            sb.AppendLine("if ($installOk -and ($updaterExe -ne '')) { Remove-Item -LiteralPath (Split-Path -Parent $updaterExe) -Recurse -Force -ErrorAction SilentlyContinue }");
            sb.AppendLine("Write-Log '=== update bootstrap end ==='");
            return sb.ToString();
        }

        /// <summary>기존 파일이 크기·해시 모두 일치하면 재사용. 해시 미제공 시 재사용 안 함 (보수적).</summary>
        private static async Task<bool> IsExistingFileValidAsync(
            string path, UpdateInfo info, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(info.DownloadSha256)) return false;
            var file = new FileInfo(path);
            if (!file.Exists) return false;
            if (info.DownloadSizeBytes > 0 && file.Length != info.DownloadSizeBytes) return false;

            using var sha = SHA256.Create();
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
            var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            return string.Equals(
                Convert.ToHexString(hash).ToLowerInvariant(),
                info.DownloadSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
