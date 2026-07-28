using System.IO;
using System.Windows.Media.Media3D;
using Occt;
using VMS.WeldTeach.Interfaces;
using VMS.WeldTeach.Models;
using static VMS.WeldTeach.Services.OcctLifetime;

namespace VMS.WeldTeach.Services;

/// <summary>
/// OpenCASCADE(Occt.NET) 기반 CAD 커널 구현.
/// STEP B-Rep 로드 → 면 삼각화(월드 좌표) + 유일 엣지 폴리라인 + 엣지-면 인접 맵을 만든다.
/// 주의: 모든 OCCT 래퍼 객체는 Keep() 으로 감싼다 — 래퍼 파이널라이저 결함 회피 (OcctLifetime 참조).
/// </summary>
public class CadKernelService : ICadKernelService
{
    static CadKernelService()
    {
        // Occt.NET 의 XS(STEP) 계열 DLL 은 이름 기반 동적 로드라 PATH 에 네이티브 폴더가
        // 없으면 SEHException 이 난다 (패키지 OcctConfiguration 은 이를 처리하지 못함).
        // 최초 OCCT 타입 사용 전에 프로세스 PATH 에 선행 등록한다.
        var nativeDir = Path.Combine(AppContext.BaseDirectory, "occt",
            Environment.Is64BitProcess ? "x64" : "x86");
        if (Directory.Exists(nativeDir))
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!path.Contains(nativeDir, StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", nativeDir + ";" + path);
        }
    }

    private const double MeshDeflection = 0.2;   // 삼각화 정밀도(mm)
    private const double EdgeSampleStep = 1.5;   // 엣지 폴리라인 샘플 간격(mm) 목표

    public CadModelData LoadStep(string path)
    {
        var reader = Keep(new STEPControl_Reader());
        var status = reader.ReadFile(path);
        if (status != IFSelect_ReturnStatus.IFSelect_RetDone)
            throw new InvalidOperationException($"STEP 파일을 읽지 못했습니다: {status}");
        reader.TransferRoots();
        var shape = Keep(reader.OneShape());
        if (shape.IsNULL)
            throw new InvalidOperationException("STEP 파일에 변환 가능한 형상이 없습니다.");
        return BuildModelData(shape, path);
    }

    public string GenerateSampleStep(string outputPath)
    {
        // T-필릿 용접 시편: 베이스 플레이트(100x60x8) + 수직 리브(60x8x30) fuse
        var basePlate = Keep(new BRepPrimAPI_MakeBox(100, 60, 8));
        var ribOrigin = Keep(new gp_Pnt(20, 26, 8));
        var rib = Keep(new BRepPrimAPI_MakeBox(ribOrigin, 60, 8, 30));
        var baseShape = Keep(basePlate.Shape);
        var ribShape = Keep(rib.Shape);
        var fuse = Keep(new BRepAlgoAPI_Fuse(baseShape, ribShape));
        var shape = Keep(fuse.IsDone ? fuse.Shape : basePlate.Shape);

        var writer = Keep(new STEPControl_Writer());
        writer.Transfer(shape, STEPControl_StepModelType.STEPControl_AsIs);
        var status = writer.Write(outputPath);
        if (status != IFSelect_ReturnStatus.IFSelect_RetDone)
            throw new InvalidOperationException($"샘플 STEP 저장 실패: {status}");
        return outputPath;
    }

    private static CadModelData BuildModelData(TopoDS_Shape shape, string sourcePath)
    {
        // 유일 면/엣지 인덱스 맵 (TopExp_Explorer 는 공유 서브셰이프를 중복 방문한다)
        TopExp.MapShapes(shape, TopAbs_ShapeEnum.TopAbs_FACE, out TopTools_IndexedMapOfShape faceMap);
        TopExp.MapShapes(shape, TopAbs_ShapeEnum.TopAbs_EDGE, out TopTools_IndexedMapOfShape edgeMap);
        TopExp.MapShapes(shape, TopAbs_ShapeEnum.TopAbs_SOLID, out TopTools_IndexedMapOfShape solidMap);
        TopExp.MapShapesAndAncestors(shape, TopAbs_ShapeEnum.TopAbs_EDGE, TopAbs_ShapeEnum.TopAbs_FACE,
            out var edgeFaceMap);
        Keep(faceMap); Keep(edgeMap); Keep(solidMap); Keep(edgeFaceMap);

        // 전체 메싱 (면별 Poly_Triangulation 생성)
        Keep(new BRepMesh_IncrementalMesh(shape, MeshDeflection, false, 0.5, true));

        var model = new CadModelData
        {
            SourcePath = sourcePath,
            SolidCount = solidMap.Extent,
            FaceCount = faceMap.Extent,
        };

        // ---- 면 메쉬 (위치 변환 적용) ----
        for (int fi = 1; fi <= faceMap.Extent; fi++)
        {
            var faceShape = Keep(faceMap.FindKey(fi));
            var face = Keep(TopoDS_Face.Cast(faceShape.NativeInstancePtr));
            var tri = Keep(BRep_Tool.Triangulation(face, out TopLoc_Location loc));
            Keep(loc);
            if (tri == null || tri.IsNULL) continue;

            var trsf = Keep(loc.Transformation);
            var mesh = new FaceMeshData { FaceId = fi, CenterNormal = FaceCenterNormal(face) };
            for (int i = 1; i <= tri.NbNodes; i++)
            {
                var node = Keep(tri.Node(i));
                var p = Keep(node.Transformed(trsf));
                mesh.Positions.Add(new Point3D(p.X, p.Y, p.Z));
            }
            for (int i = 1; i <= tri.NbTriangles; i++)
            {
                var t = Keep(tri.Triangle(i));
                t.Get(out int a, out int b, out int c);
                mesh.TriangleIndices.Add(a - 1);
                mesh.TriangleIndices.Add(b - 1);
                mesh.TriangleIndices.Add(c - 1);
            }
            model.FaceMeshes.Add(mesh);
        }

        // ---- 엣지 폴리라인 + 인접 면 + 지점별 법선 이등분 ----
        for (int ei = 1; ei <= edgeMap.Extent; ei++)
        {
            var edgeShape = Keep(edgeMap.FindKey(ei));
            var edge = Keep(TopoDS_Edge.Cast(edgeShape.NativeInstancePtr));
            var curve = Keep(new BRepAdaptor_Curve(edge));
            double t0 = curve.FirstParameter, t1 = curve.LastParameter;

            var pa = Keep(curve.Value(t0));
            var pb = Keep(curve.Value(t1));
            var chord = Dist(pa, pb);
            int n = Math.Clamp((int)Math.Ceiling(Math.Max(chord, 1.0) / EdgeSampleStep), 8, 200);

            var info = new CadEdgeInfo
            {
                EdgeId = ei,
                StartPoint = ToP(pa),
                EndPoint = ToP(pb),
                StartTangent = TangentAt(curve, t0),
                EndTangent = TangentAt(curve, t1),
                IsClosed = Dist(pa, pb) < 1e-7,
                Length = PolylineFill(curve, t0, t1, n, out var pts),
            };
            info.Points.AddRange(pts);

            // 인접 면 (edgeFaceMap 의 키 순서는 edgeMap 과 다를 수 있어 FindFromKey 사용)
            var adjFaces = new List<TopoDS_Face>();
            var faces = Keep(edgeFaceMap.FindFromKey(edgeShape));
            if (faces != null)
            {
                foreach (TopoDS_Shape fs in faces)
                {
                    Keep(fs);
                    int fid = faceMap.FindIndex(fs);
                    if (fid > 0 && !info.AdjacentFaceIds.Contains(fid))
                    {
                        info.AdjacentFaceIds.Add(fid);
                        if (adjFaces.Count < 2)
                            adjFaces.Add(Keep(TopoDS_Face.Cast(fs.NativeInstancePtr)));
                    }
                }
            }
            FillPointBisectors(edge, adjFaces, n, info.PointBisectors);
            model.Edges.Add(info);
        }
        return model;
    }

    /// <summary>
    /// 엣지 폴리라인 샘플(0..n)마다 인접 면들의 법선을 pcurve UV 로 평가해 이등분 벡터를 만든다.
    /// 곡면(원통 등) 심에서 지점별로 회전하는 올바른 토치 방향의 원천이 된다.
    /// pcurve 가 없거나 평가 실패 시 면 중심 법선으로 대체.
    /// </summary>
    private static void FillPointBisectors(TopoDS_Edge edge, List<TopoDS_Face> adjFaces, int n,
        List<Vector3D> result)
    {
        var evaluators = new List<(Geom2d_Curve? Pcurve, double T0, double T1, BRepGProp_Face Gf, Vector3D Fallback)>();
        foreach (var face in adjFaces)
        {
            Geom2d_Curve? pc = null;
            double pt0 = 0, pt1 = 0;
            try { pc = Keep(BRep_Tool.CurveOnSurface(edge, face, out pt0, out pt1)); }
            catch { /* pcurve 없음 — fallback 사용 */ }
            evaluators.Add((pc, pt0, pt1, Keep(new BRepGProp_Face(face)), FaceCenterNormal(face)));
        }

        for (int k = 0; k <= n; k++)
        {
            var sum = new Vector3D();
            foreach (var (pc, pt0, pt1, gf, fallback) in evaluators)
            {
                var nv = fallback;
                if (pc != null && !pc.IsNULL)
                {
                    try
                    {
                        var uv = Keep(pc.Value(pt0 + (pt1 - pt0) * k / n));
                        gf.Normal(uv.X, uv.Y, out gp_Pnt p, out gp_Vec gn);
                        Keep(p); Keep(gn);
                        var v = new Vector3D(gn.X, gn.Y, gn.Z);
                        if (v.Length > 1e-12) { v.Normalize(); nv = v; }
                    }
                    catch { /* 평가 실패 — fallback 유지 */ }
                }
                sum += nv;
            }
            if (sum.Length > 1e-9) sum.Normalize();
            result.Add(sum);
        }
    }

    private static double PolylineFill(BRepAdaptor_Curve curve, double t0, double t1, int n, out List<Point3D> pts)
    {
        pts = new List<Point3D>(n + 1);
        double length = 0;
        Point3D? prev = null;
        for (int k = 0; k <= n; k++)
        {
            var p = Keep(curve.Value(t0 + (t1 - t0) * k / n));
            var cur = new Point3D(p.X, p.Y, p.Z);
            if (prev.HasValue) length += (cur - prev.Value).Length;
            pts.Add(cur);
            prev = cur;
        }
        return length;
    }

    private static Vector3D TangentAt(BRepAdaptor_Curve curve, double t)
    {
        curve.D1(t, out gp_Pnt p, out gp_Vec v);
        Keep(p); Keep(v);
        var vec = new Vector3D(v.X, v.Y, v.Z);
        if (vec.Length > 1e-12) vec.Normalize();
        return vec;
    }

    private static Vector3D FaceCenterNormal(TopoDS_Face face)
    {
        BRepTools.UVBounds(face, out double umin, out double umax, out double vmin, out double vmax);
        var gf = Keep(new BRepGProp_Face(face));
        gf.Normal((umin + umax) / 2, (vmin + vmax) / 2, out gp_Pnt p, out gp_Vec n);
        Keep(p); Keep(n);
        var vec = new Vector3D(n.X, n.Y, n.Z);
        if (vec.Length > 1e-12) vec.Normalize();
        return vec;
    }

    private static Point3D ToP(gp_Pnt p) => new(p.X, p.Y, p.Z);
    private static double Dist(gp_Pnt a, gp_Pnt b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
}
