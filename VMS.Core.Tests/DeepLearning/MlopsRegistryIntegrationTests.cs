using System;
using System.IO;
using System.Threading.Tasks;
using VMS.Core.DeepLearning;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.DeepLearning
{
    /// <summary>
    /// 살아 있는 MLOps 서버에 실제로 붙어 보는 시험.
    ///
    /// <para>
    /// 단위 시험이 규약(참조 형식·캐시·해시)을 지켜 주지만, 두 리포가 정말 말이 통하는지는
    /// 붙여 봐야 안다. 서버 주소와 라인 토큰이 환경 변수로 주어질 때만 돌고, 없으면 조용히 지나간다.
    /// </para>
    /// <code>
    /// set MLOPS_URL=http://localhost:5310
    /// set MLOPS_LINE_TOKEN=ln_...
    /// set MLOPS_MODEL_ID=...        (운영 단계로 승격된 모델)
    /// dotnet test VMS.Core.Tests --filter MlopsRegistryIntegrationTests
    /// </code>
    /// </summary>
    public class MlopsRegistryIntegrationTests : IDisposable
    {
        private static string? Url => Environment.GetEnvironmentVariable("MLOPS_URL");
        private static string? Token => Environment.GetEnvironmentVariable("MLOPS_LINE_TOKEN");
        private static string? ModelId => Environment.GetEnvironmentVariable("MLOPS_MODEL_ID");
        private static bool Configured =>
            !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(ModelId);

        private readonly string _cacheRoot =
            Path.Combine(Path.GetTempPath(), "vms-mlops-it-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task 라인_토큰으로_서버에_붙는다()
        {
            if (!Configured) return;

            using var client = new ModelRegistryClient(Url!, Token!);
            var (ok, message) = await client.CheckAsync();

            Assert.True(ok, message);
            Assert.Contains("연결됨", message);
        }

        [Fact]
        public async Task 운영_단계_참조를_풀어_아티팩트를_받는다()
        {
            if (!Configured) return;

            var reference = ModelReference.ForStage(Guid.Parse(ModelId!), "production");
            var cache = new ModelArtifactCache(_cacheRoot);
            var resolver = new ModelReferenceResolver(cache, () => new ModelRegistryClient(Url!, Token!));

            // 받기 전에는 검사 경로가 아무것도 못 준다 — 네트워크를 쓰지 않기 때문이다
            Assert.Null(resolver.ToLocalPath(reference.ToString()));

            var path = await resolver.PrepareAsync(reference.ToString());

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
            // 받은 뒤에는 검사 경로가 네트워크 없이 같은 파일을 준다
            Assert.Equal(path, resolver.ToLocalPath(reference.ToString()));
            // 파일 이름이 곧 내용의 해시다
            Assert.EndsWith(".onnx", path);

            resolver.Dispose();
        }

        [Fact]
        public async Task 두_번째_준비는_네트워크를_쓰지_않는다()
        {
            if (!Configured) return;

            var reference = ModelReference.ForStage(Guid.Parse(ModelId!), "production").ToString();
            var cache = new ModelArtifactCache(_cacheRoot);
            using var resolver = new ModelReferenceResolver(cache, () => new ModelRegistryClient(Url!, Token!));

            var first = await resolver.PrepareAsync(reference);
            var written = File.GetLastWriteTimeUtc(first);
            var second = await resolver.PrepareAsync(reference);

            Assert.Equal(first, second);
            Assert.Equal(written, File.GetLastWriteTimeUtc(second));
        }

        [Fact]
        public async Task 고르는_화면이_쓸_목록을_받는다()
        {
            // VisionSetup 의 [레지스트리…] 창이 이 두 호출로 목록을 채운다
            if (!Configured) return;

            using var client = new ModelRegistryClient(Url!, Token!);

            var models = await client.ListModelsAsync();
            Assert.NotEmpty(models);
            Assert.All(models, m => Assert.NotEqual(Guid.Empty, m.Id));
            Assert.All(models, m => Assert.False(string.IsNullOrWhiteSpace(m.Name)));

            var target = Guid.Parse(ModelId!);
            var versions = await client.ListVersionsAsync(target);
            Assert.NotEmpty(versions);
            Assert.Contains(versions, v => v.Number > 0 && !string.IsNullOrWhiteSpace(v.Sha256));
            // 창은 운영 단계를 기본으로 고르므로 그 버전이 보여야 한다
            Assert.Contains(versions, v => string.Equals(v.Stage, "production", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task 작업_유형으로_목록을_걸러_준다()
        {
            // 검출 도구 설정에서 분류 모델이 보이면 잘못 고르기 쉽다
            if (!Configured) return;

            using var client = new ModelRegistryClient(Url!, Token!);
            var detection = await client.ListModelsAsync("detection");

            Assert.All(detection, m =>
                Assert.Equal("detection", m.TaskType, ignoreCase: true));
        }

        [Fact]
        public async Task 없는_모델은_이유를_말한다()
        {
            if (!Configured) return;

            using var client = new ModelRegistryClient(Url!, Token!);
            var missing = ModelReference.ForStage(Guid.NewGuid(), "production");

            var ex = await Assert.ThrowsAsync<ModelRegistryException>(() => client.ResolveAsync(missing));

            Assert.Contains("참조를 풀 수 없습니다", ex.Message);
        }

        [Fact]
        public async Task 잘못된_토큰은_거부된다()
        {
            if (!Configured) return;

            using var client = new ModelRegistryClient(Url!, "ln_no-such-token-0123456789");
            var (ok, message) = await client.CheckAsync();

            Assert.False(ok);
            Assert.Contains("토큰", message);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true); } catch (IOException) { }
        }
    }
}
