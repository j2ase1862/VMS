using System;
using System.IO;
using System.Text;
using VMS.Camera.Converters;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// StlMeshLoader 검증 (CAD 기준 3D 검사 — 로드맵 PR⑥):
    /// - 바이너리/ASCII STL 파싱
    /// - 면적 가중 표면 샘플링 결과가 메시 표면 위에 놓이는지
    /// - .vpc/.stl 확장자 분기 (LoadReferenceCloud)
    /// - 시드 고정 재현성
    /// </summary>
    public class StlMeshLoaderTests : IDisposable
    {
        private readonly string _tempDir;

        public StlMeshLoaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"stl_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        /// <summary>단위 정사각형(z=0, 삼각형 2개) 바이너리 STL 생성.</summary>
        private string WriteBinaryQuad()
        {
            var path = Path.Combine(_tempDir, "quad.stl");
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write(new byte[80]);              // header
            bw.Write(2u);                        // triangle count

            void WriteTri(float[] a, float[] b, float[] c)
            {
                for (int i = 0; i < 3; i++) bw.Write(0f);   // normal
                foreach (var v in a) bw.Write(v);
                foreach (var v in b) bw.Write(v);
                foreach (var v in c) bw.Write(v);
                bw.Write((ushort)0);                          // attribute
            }

            WriteTri(new[] { 0f, 0f, 0f }, new[] { 10f, 0f, 0f }, new[] { 10f, 10f, 0f });
            WriteTri(new[] { 0f, 0f, 0f }, new[] { 10f, 10f, 0f }, new[] { 0f, 10f, 0f });
            return path;
        }

        private string WriteAsciiQuad()
        {
            var path = Path.Combine(_tempDir, "quad_ascii.stl");
            var sb = new StringBuilder();
            sb.AppendLine("solid quad");
            void Tri(string a, string b, string c)
            {
                sb.AppendLine("  facet normal 0 0 1");
                sb.AppendLine("    outer loop");
                sb.AppendLine($"      vertex {a}");
                sb.AppendLine($"      vertex {b}");
                sb.AppendLine($"      vertex {c}");
                sb.AppendLine("    endloop");
                sb.AppendLine("  endfacet");
            }
            Tri("0 0 0", "10 0 0", "10 10 0");
            Tri("0 0 0", "10 10 0", "0 10 0");
            sb.AppendLine("endsolid quad");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Sampled_points_lie_on_mesh_surface(bool binary)
        {
            var path = binary ? WriteBinaryQuad() : WriteAsciiQuad();

            var cloud = StlMeshLoader.LoadAsPointCloud(path, targetPoints: 5000);

            Assert.Equal(5000, cloud.PointCount);
            foreach (var p in cloud.Positions)
            {
                Assert.InRange(p.X, -0.001f, 10.001f);
                Assert.InRange(p.Y, -0.001f, 10.001f);
                Assert.Equal(0f, p.Z, 3);   // 평면 메시 → 전부 z=0
            }
        }

        [Fact]
        public void Sampling_is_deterministic()
        {
            var path = WriteBinaryQuad();
            var a = StlMeshLoader.LoadAsPointCloud(path, 1000);
            var b = StlMeshLoader.LoadAsPointCloud(path, 1000);

            for (int i = 0; i < 1000; i++)
                Assert.Equal(a.Positions[i], b.Positions[i]);
        }

        [Fact]
        public void LoadReferenceCloud_dispatches_by_extension()
        {
            // .stl → 샘플링 로더
            var stl = StlMeshLoader.LoadReferenceCloud(WriteBinaryQuad(), 500);
            Assert.Equal(500, stl.PointCount);
            Assert.Contains("(STL)", stl.Name);

            // .vpc → 기존 로더 (저장 후 왕복)
            var vpcPath = Path.Combine(_tempDir, "ref.vpc");
            stl.SaveToFile(vpcPath);
            var vpc = StlMeshLoader.LoadReferenceCloud(vpcPath);
            Assert.Equal(500, vpc.PointCount);
        }

        [Fact]
        public void Invalid_file_throws()
        {
            var bad = Path.Combine(_tempDir, "bad.stl");
            File.WriteAllText(bad, "not an stl");
            Assert.ThrowsAny<Exception>(() => StlMeshLoader.LoadAsPointCloud(bad));
        }
    }
}
