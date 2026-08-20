using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// GitHubUpdateService 의 tag 파싱 + 통신 실패 시 best-effort null 반환을 검증.
    /// 실제 GitHub API 호출은 하지 않고, HttpMessageHandler stub 으로 응답을 주입.
    /// </summary>
    public class GitHubUpdateServiceTests
    {
        // ── TryParseTag ──

        [Theory]
        [InlineData("v1.1.0",    "1.1.0")]
        [InlineData("V2.0.0",    "2.0.0")]
        [InlineData("1.2.3",     "1.2.3")]
        [InlineData("v1.2.3.4",  "1.2.3.4")]
        public void TryParseTag_ValidTags_Succeeds(string input, string expected)
        {
            Assert.True(GitHubUpdateService.TryParseTag(input, out var parsed));
            Assert.Equal(new Version(expected), parsed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-version")]
        [InlineData("v1.2.3-rc1")]    // pre-release 거부 (보수적 비교 불가)
        [InlineData("v1.2.3+build1")] // build metadata 거부
        public void TryParseTag_InvalidTags_Fails(string? input)
        {
            Assert.False(GitHubUpdateService.TryParseTag(input, out _));
        }

        // ── CheckAsync 통신 실패 → null ──

        [Fact]
        public async Task CheckAsync_HttpError_ReturnsNull()
        {
            var handler = new StubHandler(req =>
                new HttpResponseMessage(HttpStatusCode.InternalServerError));
            using var http = new HttpClient(handler);

            using var service = new GitHubUpdateService(
                "owner", "repo", new Version(1, 0, 0), http);

            var result = await service.CheckAsync(CancellationToken.None);
            Assert.Null(result);
        }

        [Fact]
        public async Task CheckAsync_InvalidJson_ReturnsNull()
        {
            var handler = new StubHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not valid json")
            });
            using var http = new HttpClient(handler);

            using var service = new GitHubUpdateService(
                "owner", "repo", new Version(1, 0, 0), http);

            var result = await service.CheckAsync(CancellationToken.None);
            Assert.Null(result);
        }

        [Fact]
        public async Task CheckAsync_TagUnparseable_ReturnsNull()
        {
            var json = """
                {
                  "tag_name": "release-2026-06",
                  "html_url": "https://example.com",
                  "body": "notes",
                  "published_at": "2026-06-01T00:00:00Z",
                  "assets": []
                }
                """;
            var handler = new StubHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
            using var http = new HttpClient(handler);

            using var service = new GitHubUpdateService(
                "owner", "repo", new Version(1, 0, 0), http);

            var result = await service.CheckAsync(CancellationToken.None);
            Assert.Null(result);
        }

        // ── NormalizeSha256Digest ──

        [Theory]
        [InlineData("sha256:085d6423f52467130d2ef358b4cd57d0e1c6796ab04ea13e63b114ca09a229c1",
                    "085d6423f52467130d2ef358b4cd57d0e1c6796ab04ea13e63b114ca09a229c1")]
        [InlineData("SHA256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
                    "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
        public void NormalizeSha256Digest_Valid_ReturnsLowerHex(string input, string expected)
        {
            Assert.Equal(expected, GitHubUpdateService.NormalizeSha256Digest(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("md5:abcdef")]                 // sha256 아님
        [InlineData("sha256:tooshort")]            // 64자 미만
        [InlineData("sha256:zzzz6423f52467130d2ef358b4cd57d0e1c6796ab04ea13e63b114ca09a229c1")] // hex 아님
        public void NormalizeSha256Digest_Invalid_ReturnsEmpty(string? input)
        {
            Assert.Equal(string.Empty, GitHubUpdateService.NormalizeSha256Digest(input));
        }

        // ── CheckAsync 성공 + 비교 ──

        [Fact]
        public async Task CheckAsync_NewerRelease_ReportsUpdateAvailable()
        {
            var json = """
                {
                  "tag_name": "v1.2.0",
                  "html_url": "https://github.com/owner/repo/releases/tag/v1.2.0",
                  "body": "Release notes here",
                  "published_at": "2026-06-01T00:00:00Z",
                  "assets": [
                    { "name": "VMS-1.2.0.msi", "browser_download_url": "https://example.com/VMS-1.2.0.msi",
                      "size": 1189417252,
                      "digest": "sha256:085d6423f52467130d2ef358b4cd57d0e1c6796ab04ea13e63b114ca09a229c1" },
                    { "name": "VMS-1.2.0.msi.sha256", "browser_download_url": "https://example.com/sha" }
                  ]
                }
                """;
            var handler = new StubHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
            using var http = new HttpClient(handler);

            using var service = new GitHubUpdateService(
                "owner", "repo", new Version(1, 1, 0), http);

            var result = await service.CheckAsync(CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result!.IsUpdateAvailable);
            Assert.Equal(new Version(1, 1, 0), result.CurrentVersion);
            Assert.Equal(new Version(1, 2, 0), result.LatestVersion);
            Assert.Equal("v1.2.0", result.LatestTagName);
            Assert.Equal("https://example.com/VMS-1.2.0.msi", result.DownloadUrl);
            Assert.Equal("VMS-1.2.0.msi", result.DownloadFileName);
            Assert.Equal(1189417252L, result.DownloadSizeBytes);
            Assert.Equal("085d6423f52467130d2ef358b4cd57d0e1c6796ab04ea13e63b114ca09a229c1", result.DownloadSha256);
            Assert.Equal("https://github.com/owner/repo/releases/tag/v1.2.0", result.ReleaseUrl);
            Assert.Equal("Release notes here", result.ReleaseNotes);
        }

        [Fact]
        public async Task CheckAsync_SameVersion_ReportsNoUpdate()
        {
            var json = """
                {
                  "tag_name": "v1.1.0",
                  "html_url": "https://example.com",
                  "body": "",
                  "published_at": "2026-06-01T00:00:00Z",
                  "assets": []
                }
                """;
            var handler = new StubHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
            using var http = new HttpClient(handler);

            using var service = new GitHubUpdateService(
                "owner", "repo", new Version(1, 1, 0), http);

            var result = await service.CheckAsync(CancellationToken.None);
            Assert.NotNull(result);
            Assert.False(result!.IsUpdateAvailable);
        }

        [Fact]
        public async Task CheckAsync_OlderRelease_ReportsNoUpdate()
        {
            // 사용자가 사전 빌드(v1.2.0)를 갖고 있고 published 가 v1.0.0 인 경우.
            var json = """
                {
                  "tag_name": "v1.0.0",
                  "html_url": "https://example.com",
                  "body": "",
                  "published_at": "2026-06-01T00:00:00Z",
                  "assets": []
                }
                """;
            var handler = new StubHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
            using var http = new HttpClient(handler);

            using var service = new GitHubUpdateService(
                "owner", "repo", new Version(1, 2, 0), http);

            var result = await service.CheckAsync(CancellationToken.None);
            Assert.NotNull(result);
            Assert.False(result!.IsUpdateAvailable);
        }

        // ── 테스트 헬퍼 ──

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
