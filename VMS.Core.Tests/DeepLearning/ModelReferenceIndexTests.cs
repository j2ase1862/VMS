using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.DeepLearning
{
    /// <summary>
    /// VM-40 후속 — 참조 → 파일 대응을 캐시 폴더 색인(references.json)에 남겨,
    /// 프로세스를 다시 띄운 직후 서버가 꺼져 있어도 이미 받은 파일을 쓴다.
    /// </summary>
    public class ModelReferenceIndexTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vms-refindex-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
        }

        private static (byte[] Bytes, string Sha) Model(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return (bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        [Fact]
        public async Task Prepared_Reference_Survives_Restart_WhenServerIsDown()
        {
            var (bytes, sha) = Model("model-v3");
            var modelId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var reference = $"model://{modelId:D}@production";

            // 1) 서버가 살아 있을 때 받는다 → 색인에 남는다
            using (var server = new FakeRegistry(modelId, versionId, sha, bytes))
            using (var first = new ModelReferenceResolver(new ModelArtifactCache(_root),
                       () => new ModelRegistryClient(server.BaseUrl, "line-token")))
            {
                await first.PrepareAsync(reference);
            }
            Assert.True(File.Exists(Path.Combine(_root, ModelReferenceResolver.IndexFileName)));

            // 2) 다시 띄움 — 서버는 꺼져 있다(닿지 않는 주소)
            using var restarted = new ModelReferenceResolver(new ModelArtifactCache(_root),
                () => new ModelRegistryClient("http://127.0.0.1:1", "line-token"));

            var path = await restarted.PrepareAsync(reference);   // 서버 실패 → 색인의 캐시본

            Assert.True(File.Exists(path));
            Assert.Equal(path, restarted.ToLocalPath(reference));
            Assert.Equal(versionId, restarted.TryGetModelVersionId(reference));
        }

        [Fact]
        public void Index_IsUsed_WithoutRegistryConfig_AndNetworkFree()
        {
            var (bytes, sha) = Model("cached-only");
            var cache = new ModelArtifactCache(_root);
            var path = cache.PutAsync(sha, new MemoryStream(bytes)).GetAwaiter().GetResult();
            var reference = $"model://{Guid.NewGuid():D}@production";
            WriteIndex(new() { [reference] = sha });

            using var resolver = new ModelReferenceResolver(cache, () => null);

            Assert.Equal(path, resolver.ToLocalPath(reference));
        }

        [Fact]
        public void Index_EntryWhoseFileWasTrimmed_IsIgnored()
        {
            var (_, sha) = Model("gone");
            var reference = $"model://{Guid.NewGuid():D}@production";
            WriteIndex(new() { [reference] = sha });   // 파일은 캐시에 없다

            using var resolver = new ModelReferenceResolver(new ModelArtifactCache(_root), () => null);

            Assert.Null(resolver.ToLocalPath(reference));
        }

        [Fact]
        public void CorruptIndex_IsTreatedAsEmpty()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, ModelReferenceResolver.IndexFileName), "{ not json");

            using var resolver = new ModelReferenceResolver(new ModelArtifactCache(_root), () => null);

            Assert.Null(resolver.ToLocalPath($"model://{Guid.NewGuid():D}@production"));
        }

        [Fact]
        public void InUsePaths_OnlyProtectsReferencesUsedInThisProcess()
        {
            // 색인에 올라간 옛 참조까지 보호하면 그 파일은 영영 정리되지 않는다
            var cache = new ModelArtifactCache(_root);
            var (b1, s1) = Model("used");
            var (b2, s2) = Model("old");
            var usedPath = cache.PutAsync(s1, new MemoryStream(b1)).GetAwaiter().GetResult();
            cache.PutAsync(s2, new MemoryStream(b2)).GetAwaiter().GetResult();
            var usedRef = $"model://{Guid.NewGuid():D}@production";
            var oldRef = $"model://{Guid.NewGuid():D}@production";
            WriteIndex(new() { [usedRef] = s1, [oldRef] = s2 });

            using var resolver = new ModelReferenceResolver(cache, () => null);
            Assert.Empty(resolver.InUsePaths());

            resolver.ToLocalPath(usedRef);

            Assert.Equal(new[] { usedPath }, resolver.InUsePaths());
        }

        private void WriteIndex(Dictionary<string, string> refToSha)
        {
            Directory.CreateDirectory(_root);
            var data = new Dictionary<string, object>();
            foreach (var pair in refToSha)
                data[pair.Key] = new { sha256 = pair.Value, modelVersionId = Guid.Empty, resolvedAtUtc = DateTime.UtcNow };
            File.WriteAllText(Path.Combine(_root, ModelReferenceResolver.IndexFileName), JsonSerializer.Serialize(data));
        }

        /// <summary>loopback 최소 레지스트리 — /resolve 와 아티팩트만.</summary>
        private sealed class FakeRegistry : IDisposable
        {
            private readonly HttpListener _listener = new();
            private readonly Guid _modelId, _versionId;
            private readonly string _sha;
            private readonly byte[] _bytes;

            public string BaseUrl { get; }

            public FakeRegistry(Guid modelId, Guid versionId, string sha, byte[] bytes)
            {
                _modelId = modelId; _versionId = versionId; _sha = sha; _bytes = bytes;
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                var port = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                BaseUrl = $"http://127.0.0.1:{port}";
                _listener.Prefixes.Add(BaseUrl + "/");
                _listener.Start();
                _ = Task.Run(LoopAsync);
            }

            private async Task LoopAsync()
            {
                while (true)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { return; }
                    byte[] body;
                    if (ctx.Request.Url!.AbsolutePath.EndsWith("/resolve", StringComparison.Ordinal))
                    {
                        body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                        {
                            modelId = _modelId, modelVersionId = _versionId, number = 3, sha256 = _sha,
                            sizeBytes = _bytes.LongLength, stage = "production", artifactUrl = "/artifacts/m.onnx"
                        }));
                        ctx.Response.ContentType = "application/json";
                    }
                    else body = _bytes;
                    ctx.Response.ContentLength64 = body.Length;
                    await ctx.Response.OutputStream.WriteAsync(body);
                    ctx.Response.Close();
                }
            }

            public void Dispose()
            {
                try { _listener.Stop(); _listener.Close(); } catch { }
            }
        }
    }
}
