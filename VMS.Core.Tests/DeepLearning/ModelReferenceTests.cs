using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.DeepLearning;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.DeepLearning
{
    /// <summary>
    /// 레시피가 모델을 가리키는 방법 — <c>model://{modelId}@{version|stage}</c>.
    /// 이 형식은 MLOps 서버와 나눈 규약이라, 여기서 어긋나면 라인에 엉뚱한 모델이 내려간다.
    /// </summary>
    public class ModelReferenceTests
    {
        private static readonly Guid Id = Guid.Parse("3f2c1a9e-0000-4000-8000-000000000001");

        [Fact]
        public void 버전_참조를_읽고_다시_쓴다()
        {
            var text = $"model://{Id:D}@7";

            Assert.True(ModelReference.TryParse(text, out var reference));
            Assert.Equal(Id, reference!.ModelId);
            Assert.Equal(ModelReferenceKind.Version, reference.Kind);
            Assert.Equal(7, reference.Version);
            Assert.Equal(text, reference.ToString());
        }

        [Theory]
        [InlineData("production")]
        [InlineData("staging")]
        [InlineData("candidate")]
        public void 단계_참조를_읽는다(string stage)
        {
            Assert.True(ModelReference.TryParse($"model://{Id:D}@{stage}", out var reference));
            Assert.Equal(ModelReferenceKind.Stage, reference!.Kind);
            Assert.Equal(stage, reference.Stage);
        }

        [Fact]
        public void 단계_이름의_대소문자는_가리지_않는다()
        {
            // 사람이 손으로 넣는 값이라 Production 도 production 으로 받아 준다
            Assert.True(ModelReference.TryParse($"model://{Id:D}@Production", out var reference));
            Assert.Equal("production", reference!.Stage);
            Assert.Equal($"model://{Id:D}@production", reference.ToString());
        }

        [Theory]
        [InlineData(@"D:\models\best.onnx")]
        [InlineData("best.onnx")]
        [InlineData("")]
        [InlineData(null)]
        public void 파일_경로는_참조가_아니다(string? path)
        {
            // 기존 레시피의 절대 경로가 그대로 동작해야 한다
            Assert.False(ModelReference.IsReference(path));
            Assert.False(ModelReference.TryParse(path, out _));
        }

        [Theory]
        [InlineData("model://not-a-guid@7")]
        [InlineData("model://@production")]
        [InlineData("model://00000000-0000-0000-0000-000000000000@7")]
        [InlineData("model://3f2c1a9e-0000-4000-8000-000000000001@")]
        [InlineData("model://3f2c1a9e-0000-4000-8000-000000000001@0")]
        [InlineData("model://3f2c1a9e-0000-4000-8000-000000000001@-3")]
        [InlineData("model://3f2c1a9e-0000-4000-8000-000000000001@알수없는단계")]
        // 레지스트리에 없는 이름. 통과시키면 라인에서 서버가 400 으로 거절한다.
        [InlineData("model://3f2c1a9e-0000-4000-8000-000000000001@archived")]
        // 레지스트리에는 있지만 "이제 쓰지 말라" 는 뜻이라 레시피가 가리키면 안 된다.
        [InlineData("model://3f2c1a9e-0000-4000-8000-000000000001@retired")]
        [InlineData("model://3f2c1a9e-0000-4000-8000-000000000001")]
        public void 잘못된_참조는_예외_대신_false_다(string text)
        {
            // 레시피에는 사람이 손으로 넣은 값도 들어온다. 던지면 레시피 로드 전체가 죽는다.
            Assert.False(ModelReference.TryParse(text, out var reference));
            Assert.Null(reference);
        }

        [Theory]
        [InlineData("archived")]
        [InlineData("retired")]
        public void 레시피가_가리킬_수_없는_단계는_만들_수도_없다(string stage)
        {
            // ForStage 로 만들 수 있으면 고르는 창이 그것을 내놓을 수 있다는 뜻이다
            Assert.ThrowsAny<ArgumentException>(() => ModelReference.ForStage(Id, stage));
        }

        [Fact]
        public void 만들어_쓴_것을_그대로_다시_읽는다()
        {
            var version = ModelReference.ForVersion(Id, 12);
            var stage = ModelReference.ForStage(Id, "Production");

            Assert.True(ModelReference.TryParse(version.ToString(), out var v));
            Assert.True(ModelReference.TryParse(stage.ToString(), out var s));
            Assert.Equal(version, v);
            Assert.Equal(stage, s);
        }

        [Fact]
        public void 화면에_보여_줄_때는_짧게_쓴다()
        {
            Assert.Equal("3f2c1a9e · v7", ModelReference.ForVersion(Id, 7).ToDisplayString());
            Assert.Equal("3f2c1a9e · production", ModelReference.ForStage(Id, "production").ToDisplayString());
        }

        [Fact]
        public void 버전과_단계는_서로_다른_참조다()
        {
            Assert.NotEqual(ModelReference.ForVersion(Id, 1), ModelReference.ForStage(Id, "production"));
            Assert.Equal(ModelReference.ForVersion(Id, 1), ModelReference.ForVersion(Id, 1));
        }
    }

    /// <summary>
    /// 내려받은 모델의 로컬 캐시. 파일 이름이 곧 내용의 해시라,
    /// 여기가 어긋나면 라인에서 다른 모델로 검사하게 된다.
    /// </summary>
    public class ModelArtifactCacheTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vms-model-cache-" + Guid.NewGuid().ToString("N"));

        private static (byte[] Bytes, string Sha) Payload(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return (bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        [Fact]
        public async Task 받은_파일을_해시_이름으로_둔다()
        {
            var cache = new ModelArtifactCache(_root);
            var (bytes, sha) = Payload("onnx-bytes");

            var path = await cache.PutAsync(sha, new MemoryStream(bytes));

            Assert.True(File.Exists(path));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Equal(path, cache.TryGet(sha));
            // 한 폴더에 다 쌓지 않고 앞 두 글자로 가른다
            Assert.Equal(sha.Substring(0, 2), Path.GetFileName(Path.GetDirectoryName(path)));
        }

        [Fact]
        public async Task 해시가_다르면_캐시에_넣지_않는다()
        {
            // 검사에 쓰일 파일이라 조용히 넘어갈 수 없다
            var cache = new ModelArtifactCache(_root);
            var (bytes, _) = Payload("onnx-bytes");
            var wrong = new string('a', 64);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => cache.PutAsync(wrong, new MemoryStream(bytes)));

            Assert.Null(cache.TryGet(wrong));
            Assert.False(File.Exists(cache.PathFor(wrong)));
        }

        [Fact]
        public async Task 반쯤_받다_실패해도_찌꺼기를_남기지_않는다()
        {
            var cache = new ModelArtifactCache(_root);
            var (_, sha) = Payload("onnx-bytes");

            await Assert.ThrowsAsync<InvalidDataException>(
                () => cache.PutAsync(sha, new MemoryStream(Encoding.UTF8.GetBytes("다른 내용"))));

            var directory = Path.GetDirectoryName(cache.PathFor(sha))!;
            if (Directory.Exists(directory))
                Assert.Empty(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task 같은_것을_두_번_받지_않는다()
        {
            var cache = new ModelArtifactCache(_root);
            var (bytes, sha) = Payload("onnx-bytes");

            var first = await cache.PutAsync(sha, new MemoryStream(bytes));
            var written = File.GetLastWriteTimeUtc(first);
            var second = await cache.PutAsync(sha, new MemoryStream(bytes));

            Assert.Equal(first, second);
            Assert.Equal(written, File.GetLastWriteTimeUtc(second));
        }

        /// <summary>
        /// VMS 메인과 VisionSetup 이 같은 레시피를 같은 시점에 열면 같은 sha 를 둘이 동시에 받는다.
        /// 종전에는 Exists 와 Move 사이에서 진 쪽의 Move 가 IOException 으로 터져 "아직 내려받지 못했습니다" 가 됐다
        /// — 파일은 이미 제자리에 있는데도. 진 쪽은 이긴 파일을 그대로 써야 한다.
        /// 스트림이 마지막 바이트를 넘긴 뒤 둘이 만나는 지점(Barrier)을 두어 커밋이 정확히 겹치게 한다.
        /// </summary>
        [Fact]
        public async Task 같은_파일을_동시에_받아도_진_쪽이_터지지_않는다()
        {
            var cache = new ModelArtifactCache(_root);
            var (bytes, sha) = Payload("onnx-bytes-race");

            for (int round = 0; round < 10; round++)
            {
                var dest = cache.PathFor(sha);
                if (File.Exists(dest)) File.Delete(dest);

                using var meet = new Barrier(2);
                var a = Task.Run(() => cache.PutAsync(sha, new MeetAtEndStream(bytes, () => meet.SignalAndWait(5000))));
                var b = Task.Run(() => cache.PutAsync(sha, new MeetAtEndStream(bytes, () => meet.SignalAndWait(5000))));

                var paths = await Task.WhenAll(a, b);   // 둘 다 예외 없이 돌아와야 한다

                Assert.Equal(dest, paths[0]);
                Assert.Equal(dest, paths[1]);
                Assert.Equal(bytes, await File.ReadAllBytesAsync(dest));
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(dest)!, "*.part"));   // 찌꺼기 없음
            }
        }

        /// <summary>
        /// 결정적 재현: Exists 검사와 Move 사이에 남이 먼저 제자리에 옮겨 둔 상황.
        /// Move 는 반드시 실패하지만, 다음 회전의 Exists 가 받아 주어 이긴 파일을 돌려줘야 한다.
        /// </summary>
        [Fact]
        public async Task 커밋_직전에_남이_먼저_옮겨_두었으면_그_파일을_쓴다()
        {
            var cache = new ModelArtifactCache(_root);
            var (bytes, sha) = Payload("onnx-bytes-lost");
            var dest = cache.PathFor(sha);

            // Exists 검사는 통과했는데 Move 직전에 "이긴 쪽" 이 완전한 파일을 제자리에 두는 순간
            // (캐시의 불변식: destination 은 완전한 파일만). 보강 전에는 여기서 IOException 이 올라갔다.
            cache.BeforeMoveForTests = () => File.WriteAllBytes(dest, bytes);

            var path = await cache.PutAsync(sha, new MemoryStream(bytes));

            Assert.Equal(dest, path);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(dest));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(dest)!, "*.part"));
        }

        /// <summary>마지막 바이트를 넘긴 뒤(EOF 직전) 한 번 콜백을 부르는 스트림 — 커밋 시점을 맞추는 데 쓴다.</summary>
        private sealed class MeetAtEndStream : MemoryStream
        {
            private readonly Action _atEnd;
            private bool _fired;

            public MeetAtEndStream(byte[] bytes, Action atEnd) : base(bytes, writable: false) => _atEnd = atEnd;

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                var n = await base.ReadAsync(buffer, offset, count, ct);
                if (n == 0 && !_fired) { _fired = true; _atEnd(); }
                return n;
            }
        }

        [Fact]
        public void 없는_것은_null_이다()
        {
            var cache = new ModelArtifactCache(_root);
            Assert.Null(cache.TryGet(new string('b', 64)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("짧음")]
        [InlineData("zzzz1111zzzz1111zzzz1111zzzz1111zzzz1111zzzz1111zzzz1111zzzz1111")]
        public void 해시가_아닌_값은_거부한다(string sha)
        {
            var cache = new ModelArtifactCache(_root);
            Assert.ThrowsAny<ArgumentException>(() => cache.PathFor(sha));
        }

        [Fact]
        public async Task 상한을_넘으면_오래된_것부터_지운다()
        {
            var cache = new ModelArtifactCache(_root);
            var kept = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 5; i++)
            {
                var (bytes, sha) = Payload(new string('x', 1000) + i);
                var path = await cache.PutAsync(sha, new MemoryStream(bytes));
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow.AddHours(-10 + i));
                kept.Add(path);
            }

            var before = cache.TotalBytes();
            int removed = cache.Trim(before / 2);

            Assert.True(removed > 0);
            Assert.True(cache.TotalBytes() <= before);
            // 가장 오래 안 쓴 것이 먼저 지워진다
            Assert.False(File.Exists(kept[0]));
        }

        [Fact]
        public async Task 쓰고_있는_파일은_지우지_않는다()
        {
            var cache = new ModelArtifactCache(_root);
            var (bytes, sha) = Payload("지금 쓰는 모델");
            var inUse = await cache.PutAsync(sha, new MemoryStream(bytes));
            File.SetLastAccessTimeUtc(inUse, DateTime.UtcNow.AddYears(-1));

            cache.Trim(1, new[] { inUse });

            Assert.True(File.Exists(inUse), "레시피가 쓰는 모델을 지우면 검사가 멈춘다");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 참조 → 로컬 경로. 검사 경로는 네트워크를 쓰지 않는다는 것이 핵심 규칙이다.
    /// </summary>
    public class ModelReferenceResolverTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "vms-resolver-" + Guid.NewGuid().ToString("N"));

        private ModelReferenceResolver NewResolver() =>
            new(new ModelArtifactCache(_root), () => null);   // 레지스트리 없음 = 캐시만 보는 상태

        [Fact]
        public void 참조가_아니면_그대로_돌려준다()
        {
            // 기존 레시피의 절대 경로가 아무 일 없이 지나가야 한다
            var resolver = NewResolver();
            Assert.Equal(@"D:\models\best.onnx", resolver.ToLocalPath(@"D:\models\best.onnx"));
        }

        [Fact]
        public void 아직_안_받은_참조는_null_이다()
        {
            var resolver = NewResolver();
            Assert.Null(resolver.ToLocalPath($"model://{Guid.NewGuid():D}@production"));
        }

        [Fact]
        public async Task 서버_설정이_없으면_이유를_말하며_실패한다()
        {
            var resolver = NewResolver();

            var ex = await Assert.ThrowsAsync<ModelRegistryException>(
                () => resolver.PrepareAsync($"model://{Guid.NewGuid():D}@production"));

            Assert.Contains("설정", ex.Message);
        }

        [Fact]
        public void 레지스트리에_못_붙는_상태를_알려_준다()
        {
            Assert.False(NewResolver().CanReachRegistry);
        }

        [Fact]
        public void 준비되지_않았다는_안내에_참조가_들어_있다()
        {
            var reference = $"model://{Guid.NewGuid():D}@production";
            Assert.Contains(reference, ModelReferenceResolver.NotPreparedMessage(reference));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
        }
    }
}
