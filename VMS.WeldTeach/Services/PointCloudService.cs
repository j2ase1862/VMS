using System.Globalization;
using System.IO;
using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;

namespace VMS.WeldTeach.Services;

/// <summary>
/// 점군 입출력 + CAD 표면 샘플링 + 합성 스캔 생성.
/// 지원 포맷: VPC(VMS 자체 바이너리 — VMS [Save 3D]/VisionSetup [Save 3D Data] 산출물),
/// ASCII/binary_little_endian PLY (x,y,z float/double), XYZ/TXT/CSV (공백·쉼표 구분).
/// </summary>
public class PointCloudService
{
    // ---- 로드 ----

    public List<Point3D> LoadCloud(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".vpc" => LoadVpc(path),
            ".ply" => LoadPly(path),
            _ => LoadXyz(path),
        };
    }

    /// <summary>
    /// VMS .vpc 로드 — VMS.Camera.Models.PointCloudData.SaveToFile 과 동일 포맷.
    /// "VPC1" 매직 + count + gridW + gridH + name(길이접두 문자열) + float×3 좌표 + byte×3 색상.
    /// (참조 추가 없이 포맷을 직접 파싱해 프로젝트 독립성 유지)
    /// </summary>
    private static List<Point3D> LoadVpc(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        var sig = br.ReadBytes(4);
        if (sig.Length != 4 || sig[0] != (byte)'V' || sig[1] != (byte)'P' || sig[2] != (byte)'C' || sig[3] != (byte)'1')
            throw new InvalidOperationException("VPC 시그니처가 아닙니다 (VMS [Save 3D] 로 저장한 파일인지 확인).");

        int count = br.ReadInt32();
        _ = br.ReadInt32();   // gridWidth
        _ = br.ReadInt32();   // gridHeight
        _ = br.ReadString();  // name

        if (count < 0 || count > 100_000_000)
            throw new InvalidOperationException($"VPC 점 개수가 비정상입니다: {count}");

        var pts = new List<Point3D>(count);
        var buf = br.ReadBytes(count * 12);
        if (buf.Length < count * 12)
            throw new InvalidOperationException("VPC 좌표 데이터가 파일 길이보다 깁니다 (손상된 파일).");
        for (int i = 0; i < count; i++)
        {
            float x = BitConverter.ToSingle(buf, i * 12);
            float y = BitConverter.ToSingle(buf, i * 12 + 4);
            float z = BitConverter.ToSingle(buf, i * 12 + 8);
            pts.Add(new Point3D(x, y, z));
        }
        // 색상(byte×3)은 정합에 불필요 — 건너뜀
        return pts;
    }

    /// <summary>WeldTeach 산출 점군을 .vpc 로 저장 (라운드트립 검증·VMS 왕복용).</summary>
    public static void SaveVpc(string path, IReadOnlyList<Point3D> points, string name = "WeldTeach")
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write("VPC1"u8.ToArray());
        bw.Write(points.Count);
        bw.Write(0);   // gridWidth (unorganized)
        bw.Write(0);   // gridHeight
        bw.Write(name);
        foreach (var p in points)
        {
            bw.Write((float)p.X); bw.Write((float)p.Y); bw.Write((float)p.Z);
        }
        var colors = new byte[points.Count * 3];
        for (int i = 0; i < colors.Length; i++) colors[i] = 200;
        bw.Write(colors);
    }

    private static List<Point3D> LoadXyz(string path)
    {
        var pts = new List<Point3D>();
        foreach (var line in File.ReadLines(path))
        {
            var t = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 3) continue;
            if (double.TryParse(t[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                double.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                pts.Add(new Point3D(x, y, z));
        }
        if (pts.Count == 0) throw new InvalidOperationException("점군 파일에서 좌표를 읽지 못했습니다.");
        return pts;
    }

    private static List<Point3D> LoadPly(string path)
    {
        using var fs = File.OpenRead(path);
        using var reader = new BinaryReader(fs);

        // 헤더 (ASCII 줄 단위)
        string ReadLine()
        {
            var chars = new List<byte>();
            int b;
            while ((b = fs.ReadByte()) != -1 && b != '\n') chars.Add((byte)b);
            return System.Text.Encoding.ASCII.GetString(chars.ToArray()).TrimEnd('\r');
        }

        bool binary = false;
        int vertexCount = 0;
        var props = new List<(string Name, string Type)>();
        bool inVertexElement = false;
        string line;
        while ((line = ReadLine()) != "end_header")
        {
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            switch (t[0])
            {
                case "format":
                    binary = t[1] == "binary_little_endian";
                    if (t[1] == "binary_big_endian")
                        throw new NotSupportedException("big-endian PLY 는 지원하지 않습니다.");
                    break;
                case "element":
                    inVertexElement = t[1] == "vertex";
                    if (inVertexElement) vertexCount = int.Parse(t[2]);
                    break;
                case "property" when inVertexElement && t.Length >= 3:
                    props.Add((t[^1], t[1]));
                    break;
            }
        }
        if (vertexCount == 0) throw new InvalidOperationException("PLY 에 vertex 요소가 없습니다.");

        var pts = new List<Point3D>(vertexCount);
        if (binary)
        {
            int SizeOf(string type) => type switch
            {
                "float" or "float32" or "int" or "int32" or "uint" or "uint32" => 4,
                "double" or "float64" => 8,
                "short" or "ushort" or "int16" or "uint16" => 2,
                "char" or "uchar" or "int8" or "uint8" => 1,
                _ => throw new NotSupportedException($"PLY property type {type}"),
            };
            for (int i = 0; i < vertexCount; i++)
            {
                double x = 0, y = 0, z = 0;
                foreach (var (name, type) in props)
                {
                    double v = type is "double" or "float64"
                        ? reader.ReadDouble()
                        : SizeOf(type) == 4 && type.StartsWith("f") ? reader.ReadSingle()
                        : ReadIntAs(reader, SizeOf(type));
                    if (name == "x") x = v; else if (name == "y") y = v; else if (name == "z") z = v;
                }
                pts.Add(new Point3D(x, y, z));
            }
        }
        else
        {
            for (int i = 0; i < vertexCount; i++)
            {
                var t = ReadLine().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double Get(string n)
                {
                    int idx = props.FindIndex(p => p.Name == n);
                    return idx >= 0 && idx < t.Length
                        ? double.Parse(t[idx], CultureInfo.InvariantCulture) : 0;
                }
                pts.Add(new Point3D(Get("x"), Get("y"), Get("z")));
            }
        }
        return pts;
    }

    private static double ReadIntAs(BinaryReader r, int size) => size switch
    {
        1 => r.ReadByte(),
        2 => r.ReadInt16(),
        _ => r.ReadInt32(),
    };

    // ---- CAD 표면 샘플링 ----

    /// <summary>면 삼각 메쉬에서 면적 비례로 점을 샘플링한다 (결정적 — 고정 시드).</summary>
    public static List<Point3D> SampleModelSurface(CadModelData model, int targetCount = 15000)
        => SampleModelSurfaceDetailed(model, targetCount).Points;

    /// <summary>
    /// 표면 샘플 + 각 샘플의 원본 삼각형 — ICP 최종 품질 지표를 점-삼각형(표면) 거리로
    /// 계산하기 위한 정보. 점-점 거리는 샘플 간격이 하한을 지배해 RMSE 가 과대평가된다.
    /// </summary>
    public static (List<Point3D> Points, List<int> TriIndex, List<(Point3D A, Point3D B, Point3D C)> Triangles)
        SampleModelSurfaceDetailed(CadModelData model, int targetCount = 15000)
    {
        var tris = new List<(Point3D A, Point3D B, Point3D C, double Area)>();
        double totalArea = 0;
        foreach (var mesh in model.FaceMeshes)
        {
            for (int i = 0; i + 2 < mesh.TriangleIndices.Count; i += 3)
            {
                var a = mesh.Positions[mesh.TriangleIndices[i]];
                var b = mesh.Positions[mesh.TriangleIndices[i + 1]];
                var c = mesh.Positions[mesh.TriangleIndices[i + 2]];
                double area = Vector3D.CrossProduct(b - a, c - a).Length * 0.5;
                if (area < 1e-12) continue;
                tris.Add((a, b, c, area));
                totalArea += area;
            }
        }
        var pts = new List<Point3D>(targetCount);
        var triIdx = new List<int>(targetCount);
        var triangles = tris.Select(t => (t.A, t.B, t.C)).ToList();
        if (tris.Count == 0 || totalArea < 1e-12) return (pts, triIdx, triangles);

        var rng = new Random(20260729);
        for (int t = 0; t < tris.Count; t++)
        {
            var (a, b, c, area) = tris[t];
            int n = Math.Max(1, (int)Math.Round(targetCount * area / totalArea));
            for (int k = 0; k < n; k++)
            {
                // 균일 barycentric 샘플
                double u = rng.NextDouble(), v = rng.NextDouble();
                if (u + v > 1) { u = 1 - u; v = 1 - v; }
                pts.Add(a + u * (b - a) + v * (c - a));
                triIdx.Add(t);
            }
        }
        return (pts, triIdx, triangles);
    }

    // ---- 합성 스캔 (하드웨어 없이 정합 검증용) ----

    /// <summary>
    /// CAD 표면 샘플에 기지(旣知) 변환 + 가우시안 노이즈를 적용한 합성 스캔을 만든다.
    /// keepAboveZ 로 상면 스캔의 부분 가시성을 흉내낸다. 반환: (점군, 적용한 CAD→스캔 변환).
    /// </summary>
    public static (List<Point3D> Cloud, Matrix3D CadToScan) GenerateSyntheticScan(
        CadModelData model, double noiseSigmaMm = 0.05, double? keepAboveZ = null)
    {
        // 기지 오프셋: Z축 10° + X축 5° 회전, (12, -7, 3) mm 이동
        var m = Matrix3D.Identity;
        m.Rotate(new Quaternion(new Vector3D(0, 0, 1), 10));
        m.Rotate(new Quaternion(new Vector3D(1, 0, 0), 5));
        m.Translate(new Vector3D(12, -7, 3));

        var samples = SampleModelSurface(model, 9000);
        var rng = new Random(772026);
        double Gauss() // Box-Muller
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        var cloud = new List<Point3D>(samples.Count);
        foreach (var p in samples)
        {
            if (keepAboveZ.HasValue && p.Z < keepAboveZ.Value) continue;
            var q = m.Transform(p);
            cloud.Add(new Point3D(
                q.X + Gauss() * noiseSigmaMm,
                q.Y + Gauss() * noiseSigmaMm,
                q.Z + Gauss() * noiseSigmaMm));
        }
        return (cloud, m);
    }
}
