using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VMS.Core.Services
{
    /// <summary>
    /// 내려받은 모델 아티팩트의 로컬 캐시 (개발 문서 §5.1 배포).
    ///
    /// <para>
    /// 파일 이름이 곧 내용의 SHA-256 이다. 같은 모델을 두 번 받지 않고, 롤백해도 이전 파일이
    /// 그대로 남아 있어 네트워크 없이 되돌릴 수 있다. 서버가 죽어 있어도 캐시에 있으면 검사는 돈다.
    /// </para>
    /// <para>
    /// 쓰기는 임시 파일에 받은 뒤 해시를 확인하고 옮기는 순서다. 중간에 전원이 나가도
    /// 반쯤 받은 파일이 정상 파일 이름으로 남지 않는다 — 그 파일을 ONNX 로 열면 무슨 일이 날지 모른다.
    /// </para>
    /// </summary>
    public sealed class ModelArtifactCache
    {
        private readonly string _root;

        /// <param name="rootDirectory">
        /// 캐시 폴더. 보통 <c>AppDataPaths.GetPath("models")</c> 로 만든 경로를 넘긴다.
        /// </param>
        public ModelArtifactCache(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("캐시 폴더가 필요합니다.", nameof(rootDirectory));
            _root = Path.GetFullPath(rootDirectory);
        }

        public string Root => _root;

        /// <summary>해시로 정해지는 파일 경로. 파일이 실제로 있는지는 보지 않는다.</summary>
        public string PathFor(string sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256) || sha256.Length < 8)
                throw new ArgumentException($"잘못된 해시입니다: {sha256}", nameof(sha256));
            var normalized = sha256.Trim().ToLowerInvariant();
            foreach (var c in normalized)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    throw new ArgumentException($"해시는 16진수여야 합니다: {sha256}", nameof(sha256));
            }
            // 한 폴더에 파일 수만 개가 쌓이면 탐색기도 느려진다. 앞 두 글자로 갈라 둔다.
            return Path.Combine(_root, normalized.Substring(0, 2), normalized + ".onnx");
        }

        /// <summary>
        /// 시험 전용 — Exists 검사 뒤, Move 직전에 한 번 불린다. "남이 그 사이에 먼저 옮겨 둔" 경합을
        /// 결정적으로 재현하기 위한 자리다. 운영 코드는 절대 설정하지 않는다.
        /// </summary>
        internal Action? BeforeMoveForTests { get; set; }

        /// <summary>이미 받아 둔 파일이 있으면 그 경로, 없으면 null.</summary>
        public string? TryGet(string sha256)
        {
            var path = PathFor(sha256);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// 내용을 캐시에 넣는다. 이미 있으면 다시 쓰지 않는다 (같은 해시면 같은 내용이므로).
        /// 받은 내용의 해시가 <paramref name="sha256"/> 과 다르면 넣지 않고 예외를 던진다.
        /// </summary>
        public async Task<string> PutAsync(string sha256, Stream content, CancellationToken ct = default)
        {
            var destination = PathFor(sha256);
            if (File.Exists(destination)) return destination;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".part";

            string actual;
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[1 << 16];
                    int read;
                    while ((read = await content.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await file.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    }
                    actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                }

                if (!string.Equals(actual, sha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                {
                    // 내용이 다르면 캐시에 넣지 않는다. 검사에 쓰일 파일이라 조용히 넘어갈 수 없다.
                    throw new InvalidDataException(
                        $"내려받은 모델의 해시가 다릅니다. 기대 {sha256}, 실제 {actual}");
                }

                return await CommitAsync(temp, destination, ct).ConfigureAwait(false);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        /// <summary>
        /// 다 받고 해시까지 맞춘 temp 를 제자리로 옮긴다.
        ///
        /// <para>
        /// 같은 파일을 동시에 받는 쪽이 있다 — VMS 메인과 VisionSetup 은 레시피를 열 때 둘 다 프리페치하므로
        /// 같은 레시피를 같은 시점에 열면 같은 sha 를 두 프로세스가 받는다. Exists 와 Move 사이가 비어 있어
        /// 둘 다 통과하면 진 쪽의 Move 가 IOException 으로 터지고, 종전에는 그것이 그대로 올라가
        /// "모델 참조를 아직 내려받지 못했습니다" 가 됐다 — 파일은 이미 제자리에 있는데도.
        /// 그래서 실패하면 <b>다음 회전의 Exists 가 받아 준다</b>. 재시도 횟수보다 이 순서가 핵심이다.
        /// (MLOps 서버의 LocalDiskArtifactStorage.CommitTempAsync 와 같은 모양.)
        /// </para>
        /// <para>
        /// 진 쪽이 해시를 다시 재지 않는 근거: destination 은 오직 이 경로 — "다 받고 해시까지 맞춘 temp 를
        /// 옮기는" — 로만 생기므로, 존재하면 완전한 파일이다. 누군가 destination 에 직접 쓰기 시작하면 이
        /// 불변식이 깨지고 여기서 반쪽 파일을 조용히 내주게 된다. <b>캐시 폴더에 직접 쓰는 코드를 만들지 말 것.</b>
        /// </para>
        /// <para>
        /// 덮어쓰지 않는다 — 이긴 파일을 덮으면 그 파일을 이미 매핑해 읽고 있는 엔진이 끊긴다.
        /// 재시도 대기는 백신이 갓 쓴 temp 를 잠깐 쥐는 경우를 위한 것이고, 그 창은 좁아 복사 우회까지는 두지 않았다.
        /// </para>
        /// </summary>
        private async Task<string> CommitAsync(string temp, string destination, CancellationToken ct)
        {
            const int MaxAttempts = 6;
            for (int attempt = 0; ; attempt++)
            {
                if (File.Exists(destination)) { TryDelete(temp); return destination; }
                try
                {
                    if (attempt == 0) BeforeMoveForTests?.Invoke();
                    File.Move(temp, destination);
                    return destination;
                }
                catch (IOException) when (attempt < MaxAttempts - 1)
                {
                    // 남이 먼저 옮겼거나(다음 회전의 Exists 가 받는다) temp 가 잠깐 잠겨 있다.
                    await Task.Delay(30 * (attempt + 1), ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>캐시가 차지한 용량. 정리 정책을 만들 때 쓴다.</summary>
        public long TotalBytes()
        {
            if (!Directory.Exists(_root)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(_root, "*.onnx", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch (IOException) { }
            }
            return total;
        }

        /// <summary>
        /// 오래 쓰지 않은 파일부터 지워 상한 아래로 맞춘다.
        /// 지금 레시피가 쓰는 파일은 <paramref name="keep"/> 로 넘겨 보호한다.
        /// </summary>
        public int Trim(long maxBytes, System.Collections.Generic.IReadOnlyCollection<string>? keep = null)
        {
            if (!Directory.Exists(_root) || maxBytes <= 0) return 0;

            var files = new System.Collections.Generic.List<FileInfo>();
            foreach (var file in Directory.EnumerateFiles(_root, "*.onnx", SearchOption.AllDirectories))
            {
                try { files.Add(new FileInfo(file)); } catch (IOException) { }
            }

            long total = 0;
            foreach (var f in files) total += f.Length;
            if (total <= maxBytes) return 0;

            files.Sort((a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
            int removed = 0;
            foreach (var file in files)
            {
                if (total <= maxBytes) break;
                if (keep != null && keep.Contains(file.FullName)) continue;
                long size = file.Length;
                if (!TryDelete(file.FullName)) continue;
                total -= size;
                removed++;
            }
            return removed;
        }

        private static bool TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
