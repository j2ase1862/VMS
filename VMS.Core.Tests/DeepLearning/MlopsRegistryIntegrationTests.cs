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
    /// <para>
    /// 올리는 쪽(학습 도구)은 사람의 JWT 가 필요해 따로 켠다.
    /// 서버에 진짜 모델 계열과 버전을 남기므로 개발 서버에서만 돌린다.
    /// </para>
    /// <code>
    /// set MLOPS_JWT=eyJ...           (엔지니어 이상 계정의 토큰)
    /// set MLOPS_ONNX=D:\...\best.onnx  (레지스트리가 규약을 판별할 수 있는 진짜 모델)
    /// set MLOPS_ONNX_CLASSES=object,logo (그 모델이 실제로 담고 있는 클래스 — 순서까지 같아야 한다)
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

        // ───────────── 올리는 쪽 (학습 도구) ─────────────

        private static string? Jwt => Environment.GetEnvironmentVariable("MLOPS_JWT");
        private static string? OnnxPath => Environment.GetEnvironmentVariable("MLOPS_ONNX");
        private static bool CanUpload =>
            !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Jwt)
            && !string.IsNullOrWhiteSpace(OnnxPath) && File.Exists(OnnxPath);

        /// <summary>
        /// 그 ONNX 가 실제로 담고 있는 클래스. 서버가 모델 안의 이름과 대조하므로
        /// 여기가 틀리면 업로드가 400 으로 막힌다 — 그게 바로 아래 시험이 확인하는 규칙이다.
        /// </summary>
        private static string[] OnnxClasses =>
            (Environment.GetEnvironmentVariable("MLOPS_ONNX_CLASSES") ?? "object,logo")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>
        /// 학습 도구가 하는 그대로 — 계열을 만들고, 그 계열에 ONNX 를 새 버전으로 올린다.
        /// 올라간 버전은 Candidate 여야 한다. 바로 Production 으로 들어가면
        /// 아무도 확인하지 않은 모델이 라인에 나간다.
        /// </summary>
        [Fact]
        public async Task 학습_결과를_새_계열로_올린다()
        {
            if (!CanUpload) return;

            using var client = new ModelRegistryClient(Url!, Jwt!);
            var classes = OnnxClasses;
            var name = "it-upload-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

            var model = await client.CreateModelAsync(name, "detection", classes, "통합 시험이 만든 계열");
            Assert.NotEqual(Guid.Empty, model.Id);
            Assert.Equal(name, model.Name);
            Assert.Equal("detection", model.TaskType, ignoreCase: true);

            var version = await client.UploadVersionAsync(
                model.Id, OnnxPath!, classes, license: "AGPL-3.0", notes: "통합 시험");

            Assert.Equal(model.Id, version.ModelId);
            Assert.Equal(1, version.Number);                       // 새 계열의 첫 버전
            Assert.Equal("candidate", version.Stage, ignoreCase: true);
            Assert.True(version.SizeBytes > 0);
            Assert.False(string.IsNullOrWhiteSpace(version.Sha256));
            Assert.False(string.IsNullOrWhiteSpace(version.Format)); // 서버가 규약을 판별했다

            // 방금 올린 것이 목록에 보인다 — 창이 다음에 열릴 때 고를 수 있어야 한다
            var versions = await client.ListVersionsAsync(model.Id);
            Assert.Contains(versions, v => v.Number == version.Number && v.Sha256 == version.Sha256);
        }

        /// <summary>
        /// 클래스가 어긋나면 서버가 거절하고, 그 이유가 사람이 읽을 수 있는 문장으로 올라온다.
        /// 이름이 밀린 모델이 라인에 나가면 검사 결과의 이름이 통째로 틀린다 — 조용히 넘어가면 안 된다.
        /// </summary>
        [Fact]
        public async Task 클래스가_어긋나면_이유를_말하며_거절한다()
        {
            if (!CanUpload) return;

            using var client = new ModelRegistryClient(Url!, Jwt!);
            var name = "it-mismatch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var wrong = new[] { "없는클래스" };

            var model = await client.CreateModelAsync(name, "detection", wrong, "통합 시험 (클래스 불일치)");
            var ex = await Assert.ThrowsAsync<ModelRegistryException>(
                () => client.UploadVersionAsync(model.Id, OnnxPath!, wrong, license: "AGPL-3.0"));

            Assert.Contains("클래스", ex.Message);
            Assert.Contains("없는클래스", ex.Message);   // 무엇과 무엇이 다른지 그대로 보여 준다
        }

        /// <summary>
        /// 올릴 파일이 없으면 서버에 가기 전에 막는다 — 빈 계열만 만들어 놓고 실패하면
        /// 레지스트리에 쓰레기가 남는다.
        /// </summary>
        [Fact]
        public async Task 없는_파일은_보내기_전에_막는다()
        {
            if (!CanUpload) return;

            using var client = new ModelRegistryClient(Url!, Jwt!);
            var ex = await Assert.ThrowsAsync<ModelRegistryException>(
                () => client.UploadVersionAsync(Guid.NewGuid(), @"C:\없는경로\없는파일.onnx", OnnxClasses));

            Assert.Contains("올릴 모델 파일이 없습니다", ex.Message);
        }

        /// <summary>라인 토큰으로는 등록할 수 없다. 그 자격은 받아 가는 쪽이다.</summary>
        [Fact]
        public async Task 라인_토큰으로는_등록하지_못한다()
        {
            if (!Configured) return;

            using var client = new ModelRegistryClient(Url!, Token!);
            var ex = await Assert.ThrowsAsync<ModelRegistryException>(
                () => client.CreateModelAsync("ln-should-not-create", "detection", new[] { "logo" }));

            Assert.Contains("권한이 없습니다", ex.Message);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true); } catch (IOException) { }
        }
    }
}
