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
            // 3) msiexec 설치 (조용한 진행률 + 재부팅 금지)
            Assert.Contains("msiexec.exe", script);
            Assert.Contains("/passive", script);
            Assert.Contains("/norestart", script);
            Assert.Contains("3010", script); // 재부팅 필요도 성공으로 간주
            // 4) 서비스 복원 — MajorUpgrade 가 demand 로 재등록하므로 auto 전환 + 시작
            Assert.Contains("sc.exe config $svcName start= auto", script);
            Assert.Contains("Start-Service", script);
            // 5) VMS 재실행 (explorer 경유 — 상승 권한 미상속)
            Assert.Contains("explorer.exe", script);
            Assert.Contains(@"C:\Program Files\BODA VMS\VMS.exe", script);
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
