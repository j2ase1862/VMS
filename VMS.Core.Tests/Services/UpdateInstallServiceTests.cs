using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Models.Updates;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// UpdateInstallService 의 다운로드(SHA-256 검증·재사용·실패 처리)와
    /// 부트스트랩 스크립트 생성 내용을 검증. 실제 네트워크/msiexec 은 사용하지 않음.
    /// </summary>
    public class UpdateInstallServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public UpdateInstallServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "vms-update-tests-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { }
        }

        private static string Sha256Hex(byte[] data)
            => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        private static UpdateInfo MakeInfo(byte[] payload, string? sha256 = null, long? size = null) => new()
        {
            DownloadUrl = "https://example.com/VMS-9.9.9.msi",
            DownloadFileName = "VMS-9.9.9.msi",
            DownloadSizeBytes = size ?? payload.Length,
            DownloadSha256 = sha256 ?? Sha256Hex(payload),
        };

        // ── DownloadAsync ──

        [Fact]
        public async Task DownloadAsync_ValidPayload_SavesVerifiedFile()
        {
            var payload = Encoding.UTF8.GetBytes("fake msi payload for hashing");
            using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            }));
            using var service = new UpdateInstallService(http, _tempDir);

            var progressReports = 0;
            var progress = new SyncProgress(_ => progressReports++);
            var result = await service.DownloadAsync(MakeInfo(payload), progress);

            Assert.True(result.Ok);
            Assert.NotNull(result.FilePath);
            Assert.Equal(payload, await File.ReadAllBytesAsync(result.FilePath!));
            Assert.True(progressReports >= 1); // 완료 시점 최소 1회 보고
            Assert.False(File.Exists(result.FilePath + ".partial"));
        }

        [Fact]
        public async Task DownloadAsync_HashMismatch_FailsAndDeletesFile()
        {
            var payload = Encoding.UTF8.GetBytes("tampered payload");
            using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            }));
            using var service = new UpdateInstallService(http, _tempDir);

            var info = MakeInfo(payload, sha256: new string('a', 64));
            var result = await service.DownloadAsync(info);

            Assert.False(result.Ok);
            Assert.Contains("SHA-256", result.Error);
            Assert.False(File.Exists(Path.Combine(_tempDir, info.DownloadFileName)));
            Assert.False(File.Exists(Path.Combine(_tempDir, info.DownloadFileName + ".partial")));
        }

        [Fact]
        public async Task DownloadAsync_SizeMismatch_Fails()
        {
            var payload = Encoding.UTF8.GetBytes("short");
            using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            }));
            using var service = new UpdateInstallService(http, _tempDir);

            var result = await service.DownloadAsync(MakeInfo(payload, size: payload.Length + 100));

            Assert.False(result.Ok);
            Assert.Contains("크기", result.Error);
        }

        [Fact]
        public async Task DownloadAsync_NoMsiAsset_Fails()
        {
            using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
            using var service = new UpdateInstallService(http, _tempDir);

            var result = await service.DownloadAsync(new UpdateInfo());

            Assert.False(result.Ok);
            Assert.Contains("MSI", result.Error);
        }

        [Fact]
        public async Task DownloadAsync_HttpError_Fails()
        {
            var payload = Encoding.UTF8.GetBytes("x");
            using var http = new HttpClient(new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.NotFound)));
            using var service = new UpdateInstallService(http, _tempDir);

            var result = await service.DownloadAsync(MakeInfo(payload));

            Assert.False(result.Ok);
            Assert.Contains("404", result.Error);
        }

        [Fact]
        public async Task DownloadAsync_ExistingVerifiedFile_ReusedWithoutHttpCall()
        {
            var payload = Encoding.UTF8.GetBytes("already downloaded msi");
            var info = MakeInfo(payload);
            Directory.CreateDirectory(_tempDir);
            await File.WriteAllBytesAsync(Path.Combine(_tempDir, info.DownloadFileName), payload);

            var httpCalls = 0;
            using var http = new HttpClient(new StubHandler(_ =>
            {
                httpCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            }));
            using var service = new UpdateInstallService(http, _tempDir);

            var result = await service.DownloadAsync(info);

            Assert.True(result.Ok);
            Assert.Equal(0, httpCalls);
        }

        [Fact]
        public async Task DownloadAsync_Cancelled_ThrowsAndCleansPartial()
        {
            var payload = new byte[8 * 1024 * 1024]; // 진행 중 취소를 위해 큰 페이로드
            using var cts = new CancellationTokenSource();
            using var http = new HttpClient(new StubHandler(_ =>
            {
                cts.Cancel(); // 헤더 응답 직후 취소 → 본문 읽기에서 감지
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            }));
            using var service = new UpdateInstallService(http, _tempDir);
            var info = MakeInfo(payload);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.DownloadAsync(info, null, cts.Token));

            Assert.False(File.Exists(Path.Combine(_tempDir, info.DownloadFileName + ".partial")));
        }

        // ── BuildBootstrapScript ──

        [Fact]
        public void BuildBootstrapScript_ContainsInstallAndServiceRestoreSteps()
        {
            var script = UpdateInstallService.BuildBootstrapScript(
                msiPath: @"C:\Temp\BODA-VMS-Update\VMS-1.9.0.msi",
                vmsExePath: @"C:\Program Files\BODA VMS\VMS.exe",
                vmsPid: 4321,
                logPath: @"C:\ProgramData\BODA\VMS\update-bootstrap.log");

            // 1) VMS 종료 대기
            Assert.Contains("$vmsPid = 4321", script);
            Assert.Contains("WaitForExit", script);
            // 2) Web 서비스 사전 상태 기록
            Assert.Contains("'BodaVmsWeb'", script);
            Assert.Contains("$wasRunning", script);
            Assert.Contains("$wasAuto", script);
            // 3) 설치 — 업데이터 UI 우선 (임시 폴더 스테이징) + msiexec /passive 폴백
            Assert.Contains("VMS.Updater.exe", script);
            Assert.Contains("CommunityToolkit.Mvvm.dll", script); // 스테이징 복사 목록
            Assert.Contains("msiexec.exe", script);
            Assert.Contains("/passive", script);
            Assert.Contains("/norestart", script);
            Assert.Contains("3010", script); // 재부팅 필요도 성공으로 간주
            // 4) 서비스 복원 — MajorUpgrade 가 demand 로 재등록하므로 자동 전환 + 시작.
            //    delayed-auto 표준 (일반 auto 는 부팅 경합 SCM 7009 타임아웃 사례 — 2026-08-27)
            Assert.Contains("sc.exe config $svcName start= delayed-auto", script);
            Assert.Contains("Start-Service", script);
            // 5) VMS 재실행 (explorer 경유 — 상승 권한 미상속)
            Assert.Contains("explorer.exe", script);
            Assert.Contains(@"C:\Program Files\BODA VMS\VMS.exe", script);
        }

        [Fact]
        public void BuildBootstrapScript_WithCurrentVersion_PassesUpdaterVersionArg()
        {
            var script = UpdateInstallService.BuildBootstrapScript(
                msiPath: @"C:\Temp\BODA-VMS-Update\VMS-1.12.0.msi",
                vmsExePath: @"C:\Program Files\BODA VMS\VMS.exe",
                vmsPid: 1,
                logPath: @"C:\ProgramData\BODA\VMS\update-bootstrap.log",
                currentVersion: "1.11.0");

            Assert.Contains("'--current', '1.11.0'", script);
        }

        [Fact]
        public void BuildBootstrapScript_WithoutCurrentVersion_OmitsVersionArg()
        {
            var script = UpdateInstallService.BuildBootstrapScript(
                msiPath: @"C:\Temp\BODA-VMS-Update\VMS-1.12.0.msi",
                vmsExePath: @"C:\Program Files\BODA VMS\VMS.exe",
                vmsPid: 1,
                logPath: @"C:\ProgramData\BODA\VMS\update-bootstrap.log");

            Assert.DoesNotContain("--current", script);
        }

        [Fact]
        public void BuildBootstrapScript_EscapesSingleQuotesInPaths()
        {
            var script = UpdateInstallService.BuildBootstrapScript(
                msiPath: @"C:\O'Brien\update.msi",
                vmsExePath: @"C:\O'Brien\VMS.exe",
                vmsPid: 1,
                logPath: @"C:\ProgramData\BODA\VMS\update-bootstrap.log");

            Assert.Contains(@"'C:\O''Brien\update.msi'", script);
            Assert.Contains(@"'C:\O''Brien\VMS.exe'", script);
        }

        [Fact]
        public void BuildBootstrapScript_WithRuntimeFiles_StagesThemAndGuardsHostfxr()
        {
            var script = UpdateInstallService.BuildBootstrapScript(
                msiPath: @"C:\Temp\BODA-VMS-Update\VMS-1.28.0.msi",
                vmsExePath: @"C:\Program Files\BODA VMS\VMS.exe",
                vmsPid: 1,
                logPath: @"C:\ProgramData\BODA\VMS\update-bootstrap.log",
                currentVersion: "1.27.0",
                updaterRuntimeFiles: new[] { "hostfxr.dll", "coreclr.dll", "System.Runtime.dll" });

            // self-contained 업데이터 스테이징 — 런타임 팩 파일을 함께 복사
            Assert.Contains("'hostfxr.dll',", script);
            Assert.Contains("'coreclr.dll',", script);
            Assert.Contains("'System.Runtime.dll',", script);
            Assert.Contains("foreach ($rf in $updaterRuntimeFiles)", script);
            Assert.Contains("Copy-Item -LiteralPath (Join-Path $vmsDir $rf)", script);
            // 설치본이 self-contained(hostfxr.dll 존재)인데 스테이징에 빠졌으면 업데이터 대신 msiexec
            Assert.Contains("(Join-Path $updDir 'hostfxr.dll')", script);
            Assert.Contains("needs hostfxr.dll", script);
        }

        [Fact]
        public void BuildBootstrapScript_UpdaterNonMsiExitCode_FallsBackToMsiexec()
        {
            var script = UpdateInstallService.BuildBootstrapScript(
                msiPath: @"C:\Temp\BODA-VMS-Update\VMS-1.28.0.msi",
                vmsExePath: @"C:\Program Files\BODA VMS\VMS.exe",
                vmsPid: 1,
                logPath: @"C:\ProgramData\BODA\VMS\update-bootstrap.log");

            // 0x80008083(-2147450749, hostfxr 부재) 같은 음수/범위 밖 종료 코드는 MSI 가 실행되지 않은 것 → 재시도
            Assert.Contains("if (($code -lt 0) -or ($code -gt 3010))", script);
            Assert.Contains("retrying via msiexec", script);
            Assert.Contains("function Install-ViaMsiexec", script);
            // 런타임 목록이 없어도 스크립트는 유효 (빈 배열)
            Assert.Contains("$updaterRuntimeFiles = @(", script);
        }

        // ── ReadUpdaterRuntimeFiles ──

        [Fact]
        public void ReadUpdaterRuntimeFiles_SelfContainedDepsJson_ReturnsRuntimePackFiles()
        {
            Directory.CreateDirectory(_tempDir);
            var path = Path.Combine(_tempDir, "VMS.Updater.deps.json");
            File.WriteAllText(path, """
                {
                  "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0/win-x64" },
                  "targets": {
                    ".NETCoreApp,Version=v8.0": {},
                    ".NETCoreApp,Version=v8.0/win-x64": {
                      "VMS.Updater/1.0.0": { "runtime": { "VMS.Updater.dll": {} } },
                      "CommunityToolkit.Mvvm/8.4.0": { "runtime": { "lib/net8.0/CommunityToolkit.Mvvm.dll": {} } },
                      "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/8.0.28": {
                        "runtime": { "System.Runtime.dll": {}, "System.Private.CoreLib.dll": {} },
                        "native": { "hostfxr.dll": {}, "coreclr.dll": {}, "System.Runtime.dll": {} }
                      },
                      "runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/8.0.28": {
                        "runtime": { "PresentationCore.dll": {} },
                        "native": { "wpfgfx_cor3.dll": {} }
                      }
                    }
                  }
                }
                """);

            var files = UpdateInstallService.ReadUpdaterRuntimeFiles(path);

            Assert.Equal(
                new[] { "System.Runtime.dll", "System.Private.CoreLib.dll", "hostfxr.dll", "coreclr.dll", "PresentationCore.dll", "wpfgfx_cor3.dll" },
                files);
            Assert.DoesNotContain("VMS.Updater.dll", files);                    // 앱 자체는 VMS.Updater.* 복사로 처리
            Assert.DoesNotContain(files, f => f.Contains("CommunityToolkit"));   // 패키지 dll 은 별도 복사
        }

        [Fact]
        public void ReadUpdaterRuntimeFiles_FrameworkDependentOrMissing_ReturnsEmpty()
        {
            Directory.CreateDirectory(_tempDir);
            var fdd = Path.Combine(_tempDir, "fdd.deps.json");
            File.WriteAllText(fdd, """
                { "targets": { ".NETCoreApp,Version=v8.0": { "VMS.Updater/1.0.0": { "runtime": { "VMS.Updater.dll": {} } } } } }
                """);
            var broken = Path.Combine(_tempDir, "broken.deps.json");
            File.WriteAllText(broken, "{ not json");

            Assert.Empty(UpdateInstallService.ReadUpdaterRuntimeFiles(fdd));
            Assert.Empty(UpdateInstallService.ReadUpdaterRuntimeFiles(broken));
            Assert.Empty(UpdateInstallService.ReadUpdaterRuntimeFiles(Path.Combine(_tempDir, "missing.deps.json")));
        }

        [Fact]
        public void ReadUpdaterRuntimeFiles_RealReleaseDepsJson_IncludesHostfxr()
        {
            // 실제 빌드 산출물이 있을 때만 (dev PC) — 런타임 팩 목록에 hostfxr.dll 이 포함되는지 확인
            var real = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "VMS", "bin", "Release", "net8.0-windows7.0", "VMS.Updater.deps.json");
            if (!File.Exists(real)) return;

            var files = UpdateInstallService.ReadUpdaterRuntimeFiles(real);

            Assert.Contains("hostfxr.dll", files);
            Assert.Contains("hostpolicy.dll", files);
            Assert.Contains("coreclr.dll", files);
            Assert.Contains("PresentationFramework.dll", files);
        }

        // ── LaunchInstaller ──

        [Fact]
        public void LaunchInstaller_MissingMsi_Fails()
        {
            using var service = new UpdateInstallService(new HttpClient(new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK))), _tempDir);

            var result = service.LaunchInstaller(Path.Combine(_tempDir, "missing.msi"));

            Assert.False(result.Ok);
            Assert.False(result.UacDeclined);
            Assert.Contains("찾을 수 없습니다", result.Error);
        }

        // ── 테스트 헬퍼 ──

        /// <summary>SynchronizationContext 없이 즉시 콜백하는 IProgress (Progress&lt;T&gt; 는 포스팅 지연).</summary>
        private sealed class SyncProgress : IProgress<UpdateDownloadProgress>
        {
            private readonly Action<UpdateDownloadProgress> _handler;
            public SyncProgress(Action<UpdateDownloadProgress> handler) => _handler = handler;
            public void Report(UpdateDownloadProgress value) => _handler(value);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(_responder(request));
        }
    }
}
