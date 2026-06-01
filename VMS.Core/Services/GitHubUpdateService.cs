using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models.Updates;
using VMS.Core.Security;

namespace VMS.Core.Services
{
    /// <summary>
    /// GitHub Releases API 를 호출해 최신 릴리스를 조회하는 IUpdateService 구현.
    ///
    /// 엔드포인트: https://api.github.com/repos/{owner}/{repo}/releases/latest
    /// public repo 라 익명 호출 가능 (rate limit 60 req/h/IP — VMS 1대 매 시작 1회라 충분).
    ///
    /// 통신/파싱 실패 시 예외를 발산하지 않고 null 반환 (best-effort).
    /// User-Agent 헤더는 GitHub API 가 요구 — HttpClientPolicy.Build 가 자동 부여.
    /// </summary>
    public sealed class GitHubUpdateService : IUpdateService
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        };

        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly Version _currentVersion;
        private bool _disposed;

        /// <summary>
        /// 운영 ctor — 호출 어셈블리 버전을 자동으로 사용.
        /// </summary>
        public GitHubUpdateService(string owner, string repo)
            : this(owner, repo, ResolveCurrentVersion(), httpClient: null)
        {
        }

        /// <summary>
        /// 테스트 친화 ctor — 임의 버전 / HttpClient 주입 가능. internal 이 아닌 이유:
        /// 다른 어셈블리(테스트 외)에서 mock 구현 시 사용 가능하게.
        /// </summary>
        public GitHubUpdateService(string owner, string repo, Version currentVersion, HttpClient? httpClient)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("owner required", nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentException("repo required", nameof(repo));

            _apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            _currentVersion = currentVersion;
            _httpClient = httpClient ?? HttpClientPolicy.Build(RequestTimeout);
        }

        public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _apiUrl);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[UpdateCheck] HTTP {(int)response.StatusCode} from {_apiUrl}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var release = JsonSerializer.Deserialize<GitHubReleaseDto>(json, JsonOptions);
                if (release is null) return null;

                if (!TryParseTag(release.TagName, out var latestVersion))
                {
                    Debug.WriteLine($"[UpdateCheck] Unable to parse tag_name='{release.TagName}'");
                    return null;
                }

                var msiAsset = FindMsiAsset(release.Assets);

                return new UpdateInfo
                {
                    CurrentVersion = _currentVersion,
                    LatestVersion = latestVersion,
                    LatestTagName = release.TagName ?? string.Empty,
                    DownloadUrl = msiAsset?.BrowserDownloadUrl ?? string.Empty,
                    ReleaseUrl = release.HtmlUrl ?? string.Empty,
                    ReleaseNotes = release.Body ?? string.Empty,
                    PublishedAt = release.PublishedAt ?? DateTime.MinValue,
                };
            }
            catch (OperationCanceledException)
            {
                // 호출 측 cancel — silent.
                return null;
            }
            catch (Exception ex)
            {
                // 네트워크/파싱 실패 best-effort — null 반환.
                Debug.WriteLine($"[UpdateCheck] {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }

        /// <summary>
        /// "v1.2.3" / "1.2.3" / "v1.2.3.4" 등 다양한 형태의 tag_name 을 Version 으로 파싱.
        /// pre-release 접미사(-rc1, +build 등) 가 있으면 실패로 처리 → null.
        /// </summary>
        internal static bool TryParseTag(string? tagName, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(tagName)) return false;

            var trimmed = tagName.TrimStart('v', 'V');
            // pre-release / build metadata 가 붙으면 단순 비교가 불가능 — 보수적으로 거부.
            if (trimmed.Contains('-') || trimmed.Contains('+')) return false;

            return Version.TryParse(trimmed, out version!);
        }

        private static GitHubAssetDto? FindMsiAsset(GitHubAssetDto[]? assets)
        {
            if (assets is null || assets.Length == 0) return null;
            foreach (var a in assets)
            {
                if (!string.IsNullOrEmpty(a.Name) &&
                    a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    return a;
                }
            }
            return null;
        }

        private static Version ResolveCurrentVersion()
        {
            // EntryAssembly 의 InformationalVersion 우선(Directory.Build.props 의 Version 반영),
            // 없거나 파싱 실패 시 AssemblyVersion 으로 폴백.
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // InformationalVersion 은 "1.1.0+abc123" 형태일 수 있음 — '+' 이전만 사용.
                var clean = info.Split('+', '-')[0];
                if (Version.TryParse(clean, out var parsed)) return parsed;
            }
            return asm.GetName().Version ?? new Version(0, 0, 0);
        }

        // ── GitHub Releases API DTOs ──

        private sealed class GitHubReleaseDto
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; }

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("published_at")]
            public DateTime? PublishedAt { get; set; }

            [JsonPropertyName("assets")]
            public GitHubAssetDto[]? Assets { get; set; }
        }

        private sealed class GitHubAssetDto
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}
