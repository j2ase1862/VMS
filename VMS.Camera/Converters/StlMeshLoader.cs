using System.Globalization;
using System.IO;
using System.Numerics;
using VMS.Camera.Models;

namespace VMS.Camera.Converters
{
    /// <summary>
    /// STL(CAD 메시) 파일을 점군으로 변환하는 로더 — CAD 기반 3D 검사용.
    /// 바이너리/ASCII STL 모두 지원. 삼각형 표면을 면적 가중 랜덤 샘플링해
    /// PointCloudData 로 만들며, Registration/Deviation 의 기준(Reference)으로
    /// .vpc 스캔 대신 CAD 원본을 쓸 수 있게 한다.
    /// 시드 고정 — 같은 파일은 항상 같은 점군 (기준 재현성).
    /// </summary>
    public static class StlMeshLoader
    {
        private const int RandomSeed = 12345;

        /// <summary>
        /// 기준 점군 로드 — 확장자에 따라 .stl(표면 샘플링) / .vpc 를 자동 분기.
        /// Registration/Deviation 툴의 공용 진입점.
        /// </summary>
        public static PointCloudData LoadReferenceCloud(string path, int targetPoints = 200_000)
        {
            return Path.GetExtension(path).ToLowerInvariant() == ".stl"
                ? LoadAsPointCloud(path, targetPoints)
                : PointCloudData.LoadFromFile(path);
        }

        /// <summary>STL 메시 → 표면 샘플링 점군 (targetPoints 개, 면적 가중).</summary>
        public static PointCloudData LoadAsPointCloud(string path, int targetPoints = 200_000)
        {
            var triangles = LoadTriangles(path);
            if (triangles.Count == 0)
                throw new InvalidDataException("STL에서 삼각형을 읽지 못했습니다.");

            // 삼각형 면적 → 누적 분포 (면적 가중 샘플링)
            var cumulative = new double[triangles.Count];
            double totalArea = 0;
            for (int i = 0; i < triangles.Count; i++)
            {
                var (a, b, c) = triangles[i];
                totalArea += Vector3.Cross(b - a, c - a).Length() * 0.5;
                cumulative[i] = totalArea;
            }

            if (totalArea <= 0)
            {
                // 퇴화 메시(면적 0) — 꼭짓점 자체를 점군으로 폴백
                var verts = new Vector3[triangles.Count * 3];
                for (int i = 0; i < triangles.Count; i++)
                {
                    verts[i * 3] = triangles[i].A;
                    verts[i * 3 + 1] = triangles[i].B;
                    verts[i * 3 + 2] = triangles[i].C;
                }
                return BuildCloud(verts, path);
            }

            var rand = new Random(RandomSeed);
            var positions = new Vector3[targetPoints];
            for (int i = 0; i < targetPoints; i++)
            {
                // 면적 비례로 삼각형 선택 (누적 배열 이진 탐색)
                double r = rand.NextDouble() * totalArea;
                int t = Array.BinarySearch(cumulative, r);
                if (t < 0) t = ~t;
                if (t >= triangles.Count) t = triangles.Count - 1;

                // 균일 barycentric 샘플
                var (a, b, c) = triangles[t];
                float r1 = MathF.Sqrt((float)rand.NextDouble());
                float r2 = (float)rand.NextDouble();
                positions[i] = (1 - r1) * a + r1 * (1 - r2) * b + r1 * r2 * c;
            }

            return BuildCloud(positions, path);
        }

        private static PointCloudData BuildCloud(Vector3[] positions, string path)
        {
            var colors = new System.Windows.Media.Color[positions.Length];
            var gray = System.Windows.Media.Color.FromRgb(180, 180, 180);
            Array.Fill(colors, gray);

            return new PointCloudData
            {
                Name = Path.GetFileNameWithoutExtension(path) + " (STL)",
                Positions = positions,
                Colors = colors
            };
        }

        // ── STL 파싱 ──

        internal readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C);

        internal static List<Triangle> LoadTriangles(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 15)
                throw new InvalidDataException("STL 파일이 너무 작습니다.");

            // 바이너리 판별 — 헤더(80) + 개수(4) + 50바이트×N 이 파일 크기와 일치하는지
            if (bytes.Length >= 84)
            {
                uint triCount = BitConverter.ToUInt32(bytes, 80);
                long expected = 84L + 50L * triCount;
                if (triCount > 0 && expected == bytes.Length)
                    return ParseBinary(bytes, (int)triCount);
            }

            return ParseAscii(bytes);
        }

        private static List<Triangle> ParseBinary(byte[] bytes, int triCount)
        {
            var tris = new List<Triangle>(triCount);
            int offset = 84;
            for (int i = 0; i < triCount; i++)
            {
                // 법선 12바이트 건너뜀 → 꼭짓점 3개 × 12바이트 → 속성 2바이트
                int p = offset + 12;
                var a = ReadVector(bytes, p);
                var b = ReadVector(bytes, p + 12);
                var c = ReadVector(bytes, p + 24);
                tris.Add(new Triangle(a, b, c));
                offset += 50;
            }
            return tris;
        }

        private static Vector3 ReadVector(byte[] bytes, int offset) => new(
            BitConverter.ToSingle(bytes, offset),
            BitConverter.ToSingle(bytes, offset + 4),
            BitConverter.ToSingle(bytes, offset + 8));

        private static List<Triangle> ParseAscii(byte[] bytes)
        {
            var tris = new List<Triangle>();
            var verts = new List<Vector3>(3);

            using var reader = new StreamReader(new MemoryStream(bytes));
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                verts.Add(new Vector3(
                    float.Parse(parts[1], CultureInfo.InvariantCulture),
                    float.Parse(parts[2], CultureInfo.InvariantCulture),
                    float.Parse(parts[3], CultureInfo.InvariantCulture)));

                if (verts.Count == 3)
                {
                    tris.Add(new Triangle(verts[0], verts[1], verts[2]));
                    verts.Clear();
                }
            }
            return tris;
        }
    }
}
