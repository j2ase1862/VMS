using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Services;
using VMS.Interfaces;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// VM-40 — VMS 메인이 레시피를 열 때 model:// 참조 모델을 받아 둔다.
    /// 종전에는 VisionSetup 만 받아, VMS 검사에서 참조 도구가 늘 "아직 내려받지 못했습니다" 로 불합격했다.
    /// </summary>
    public class ModelReferencePreparerTests : IDisposable
    {
        private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"model_prep_{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true); } catch { }
        }

        private static Recipe RecipeWith(params object[] modelPaths)
        {
            var step = new InspectionStep { Name = "Step 1" };
            foreach (var mp in modelPaths)
            {
                var tool = new ToolConfig { ToolType = "DetectionTool", Name = "Detection" };
                tool.Parameters["ModelPath"] = mp;
                step.Tools.Add(tool);
            }
            var recipe = new Recipe { Name = "R1" };
            recipe.Steps.Add(step);
            return recipe;
        }

        [Fact]
        public void CollectReferences_OnlyReferences_Distinct_AndJsonElement()
        {
            var id = Guid.NewGuid();
            var reference = $"model://{id}@production";
            var json = JsonDocument.Parse(JsonSerializer.Serialize(reference)).RootElement;

            var refs = ModelReferencePreparer.CollectReferences(
                RecipeWith(reference, json, @"C:\models\abs.onnx", ""));

            Assert.Equal(new[] { reference }, refs);
        }

        [Fact]
        public async Task Prepare_DownloadsReference_SoInspectionCanFindIt_AndTrimKeepsIt()
        {
            var bytes = Encoding.UTF8.GetBytes("fake-onnx-model-bytes");
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var modelId = Guid.NewGuid();
            var reference = $"model://{modelId}@production";

            // 캐시에 이미 있는 다른 모델(오래된 것) — 상한을 넘기면 이쪽이 지워져야 한다
            var cache = new ModelArtifactCache(_cacheDir);
            var otherBytes = new byte[4096];
            var otherSha = Convert.ToHexString(SHA256.HashData(otherBytes)).ToLowerInvariant();
            var otherPath = await cache.PutAsync(otherSha, new MemoryStream(otherBytes));
            File.SetLastAccessTimeUtc(otherPath, DateTime.UtcNow.AddDays(-30));

            using var server = new FakeRegistry(modelId, sha, bytes);
            using var resolver = new ModelReferenceResolver(cache, () => new ModelRegistryClient(server.BaseUrl, "line-token"));
            var log = new RecordingLog();
            var preparer = new ModelReferencePreparer(log, cacheMaxBytes: bytes.Length, resolver: () => resolver);

            Assert.Null(resolver.ToLocalPath(reference));   // 받기 전 — 검사는 '아직 내려받지 못했습니다'

            await preparer.PrepareForRecipeAsync(RecipeWith(reference));

            var local = resolver.ToLocalPath(reference);
            Assert.NotNull(local);
            Assert.True(File.Exists(local));
            Assert.False(File.Exists(otherPath), "상한을 넘긴 캐시에서 쓰지 않는 모델이 정리되지 않았습니다");
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Info && e.Message.Contains("준비 완료"));
        }

        [Fact]
        public async Task PrepareFailure_LogsWarning_AndSkipsTrim()
        {
            var cache = new ModelArtifactCache(_cacheDir);
            var otherBytes = new byte[4096];
            var otherSha = Convert.ToHexString(SHA256.HashData(otherBytes)).ToLowerInvariant();
            var otherPath = await cache.PutAsync(otherSha, new MemoryStream(otherBytes));

            // 서버 설정이 없는 라인 — 참조를 풀 수 없다
            using var resolver = new ModelReferenceResolver(cache, () => null);
            var log = new RecordingLog();
            var preparer = new ModelReferencePreparer(log, cacheMaxBytes: 1, resolver: () => resolver);
            var reference = $"model://{Guid.NewGuid()}@production";

            await preparer.PrepareForRecipeAsync(RecipeWith(reference));   // 예외 없이 끝나야 한다

            var warn = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
            Assert.Contains(reference, warn.Message);
            Assert.Contains("불합격", warn.Message);
            // 무엇을 쓰는지 다 모르면 정리하지 않는다 — 그 참조가 가리키는 파일일 수도 있다
            Assert.True(File.Exists(otherPath));
        }

        [Fact]
        public async Task OnlyMissing_SkipsAlreadyPrepared()
        {
            var bytes = Encoding.UTF8.GetBytes("model-v1");
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var modelId = Guid.NewGuid();
            var reference = $"model://{modelId}@production";

            using var server = new FakeRegistry(modelId, sha, bytes);
            using var resolver = new ModelReferenceResolver(new ModelArtifactCache(_cacheDir),
                () => new ModelRegistryClient(server.BaseUrl, "line-token"));
            var preparer = new ModelReferencePreparer(new RecordingLog(), cacheMaxBytes: 0, resolver: () => resolver);

            await preparer.PrepareForRecipeAsync(RecipeWith(reference));
            var resolvesAfterLoad = server.ResolveCount;

            await preparer.PrepareForRecipeAsync(RecipeWith(reference), onlyMissing: true);

            Assert.Equal(resolvesAfterLoad, server.ResolveCount);   // 이미 받은 참조는 서버에 다시 묻지 않는다
        }

        // ── 대역 ──

        private sealed class RecordingLog : ISystemLogService
        {
            public List<LogEntry> Entries { get; } = new();
            public ObservableCollection<LogEntry> LogEntries { get; } = new();
            public void Log(string message, LogLevel level = LogLevel.Info, string source = "")
            {
                lock (Entries) Entries.Add(new LogEntry { Message = message, Level = level, Source = source });
            }
            public void Clear() { }
        }

        /// <summary>loopback 에 띄우는 최소 레지스트리 — /resolve 와 아티팩트만.</summary>
        private sealed class FakeRegistry : IDisposable
        {
            private readonly HttpListener _listener = new();
            private readonly CancellationTokenSource _cts = new();
            private readonly Guid _modelId;
            private readonly string _sha;
            private readonly byte[] _bytes;
            private int _resolveCount;

            public string BaseUrl { get; }
            public int ResolveCount => Volatile.Read(ref _resolveCount);

            public FakeRegistry(Guid modelId, string sha, byte[] bytes)
            {
                _modelId = modelId;
                _sha = sha;
                _bytes = bytes;
                var port = FreePort();
                BaseUrl = $"http://127.0.0.1:{port}";
                _listener.Prefixes.Add(BaseUrl + "/");
                _listener.Start();
                _ = Task.Run(LoopAsync);
            }

            private static int FreePort()
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                var port = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                return port;
            }

            private async Task LoopAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { return; }

                    var path = ctx.Request.Url!.AbsolutePath;
                    byte[] body;
                    if (path.EndsWith("/resolve", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref _resolveCount);
                        body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                        {
                            modelId = _modelId,
                            modelVersionId = Guid.NewGuid(),
                            number = 3,
                            sha256 = _sha,
                            sizeBytes = _bytes.LongLength,
                            stage = "production",
                            artifactUrl = "/artifacts/model.onnx"
                        }));
                        ctx.Response.ContentType = "application/json";
                    }
                    else
                    {
                        body = _bytes;
                        ctx.Response.ContentType = "application/octet-stream";
                    }
                    ctx.Response.ContentLength64 = body.Length;
                    await ctx.Response.OutputStream.WriteAsync(body);
                    ctx.Response.Close();
                }
            }

            public void Dispose()
            {
                _cts.Cancel();
                try { _listener.Stop(); _listener.Close(); } catch { }
            }
        }
    }
}
