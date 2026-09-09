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

                // 같은 파일을 동시에 받은 다른 스레드가 이미 옮겼을 수 있다 — 그러면 그 파일을 쓴다.
                if (File.Exists(destination)) { TryDelete(temp); return destination; }
                File.Move(temp, destination);
                return destination;
            }
            catch
            {
                TryDelete(temp);
                throw;
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
