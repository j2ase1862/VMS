using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.SurfaceAnalysis;

// ─────────────────────────────────────────────────────────────────────────────
// 포토메트릭 스테레오 검증
//   1) 알려진 높이장(구면 캡 + 미세 딤플)에서 정답 법선을 계산
//   2) N개 조명 방향으로 Lambertian 렌더링 → 합성 이미지 N장 저장
//   3) 실제 PhotometricStereoTool(A 방식)을 합성 이미지에 실행
//   4) NormalMap 출력을 디코드해 정답 법선과 각도 오차 비교
//   5) DefectEnhanced / Albedo 출력도 저장(육안 확인용)
// ─────────────────────────────────────────────────────────────────────────────

string outDir = args.Length > 0 ? args[0] : "ps_synth";
Directory.CreateDirectory(outDir);

const int N = 256;            // 이미지 한 변
const double cx = 128, cy = 128;
const double R = 100;         // 구 반지름(px)
const double albedo = 0.85;

// 미세 딤플(결함) — 구 표면에 작은 패임
const double dimpleX = 168, dimpleY = 108, dimpleAmp = 6.0, dimpleSigma = 7.0;

// 높이장 z(x,y): 구면 캡 - 딤플. 평면부는 z=0.
double Height(double x, double y)
{
    double dx = x - cx, dy = y - cy;
    double r2 = dx * dx + dy * dy;
    double z = r2 < R * R ? Math.Sqrt(R * R - r2) : 0.0;
    double ddx = x - dimpleX, ddy = y - dimpleY;
    z -= dimpleAmp * Math.Exp(-(ddx * ddx + ddy * ddy) / (2 * dimpleSigma * dimpleSigma));
    return z;
}

// 높이장 기울기에서 정답 법선 n = (-zx, -zy, 1)/|·| (중심차분)
(double nx, double ny, double nz) GroundTruthNormal(int x, int y)
{
    double zx = (Height(x + 1, y) - Height(x - 1, y)) * 0.5;
    double zy = (Height(x, y + 1) - Height(x, y - 1)) * 0.5;
    double nx = -zx, ny = -zy, nz = 1.0;
    double m = Math.Sqrt(nx * nx + ny * ny + nz * nz);
    return (nx / m, ny / m, nz / m);
}

// 조명 방향 N개: 고도 50°, 방위 0/90/180/270° + 정수리(top) 1개 → 총 5개
double el = 50 * Math.PI / 180;
var lights = new List<(double x, double y, double z)>();
foreach (var azDeg in new[] { 0, 90, 180, 270 })
{
    double az = azDeg * Math.PI / 180;
    lights.Add((Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el)));
}
lights.Add((0, 0, 1)); // top

// 정답 법선 캐시
var gt = new (double nx, double ny, double nz)[N, N];
for (int y = 0; y < N; y++)
    for (int x = 0; x < N; x++)
        gt[y, x] = GroundTruthNormal(x, y);

// ── 합성 이미지 N장 렌더 + 저장 ──
var paths = new List<string>();
for (int i = 0; i < lights.Count; i++)
{
    var (lx, ly, lz) = lights[i];
    using var img = new Mat(N, N, MatType.CV_8UC1);
    var idx = img.GetGenericIndexer<byte>();
    for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            var (nx, ny, nz) = gt[y, x];
            double dot = nx * lx + ny * ly + nz * lz;
            double I = albedo * Math.Max(0, dot);     // Lambertian + 자기그림자
            idx[y, x] = (byte)Math.Clamp(I * 255.0, 0, 255);
        }
    string p = Path.GetFullPath(Path.Combine(outDir, $"light_{i}.png"));
    Cv2.ImWrite(p, img);
    paths.Add(p);
    Console.WriteLine($"  rendered {Path.GetFileName(p)}  L=({lx,6:F3},{ly,6:F3},{lz,6:F3})");
}

// ── 실제 툴 실행 (A 방식) ──
(Mat outImg, string label) Run(PsOutputType type)
{
    var tool = new PhotometricStereoTool { OutputType = type };
    for (int i = 0; i < lights.Count; i++)
        tool.Lights.Add(new LightSample
        { Lx = lights[i].x, Ly = lights[i].y, Lz = lights[i].z, ImagePath = paths[i] });
    var r = tool.Execute(new Mat()); // A 방식이라 inputImage 미사용
    if (!r.Success || r.OutputImage == null)
        throw new Exception($"{type} 실행 실패: {r.Message}");
    Console.WriteLine($"  {type,-14} → {r.Message}  ({tool.ExecutionTime:F1} ms)");
    return (r.OutputImage, type.ToString());
}

Console.WriteLine("\n[ 툴 실행 ]");
var (normalMap, _) = Run(PsOutputType.NormalMap);
var (defect, _) = Run(PsOutputType.DefectEnhanced);
var (albedoImg, _) = Run(PsOutputType.Albedo);

Cv2.ImWrite(Path.Combine(outDir, "out_normalmap.png"), normalMap);
Cv2.ImWrite(Path.Combine(outDir, "out_defect.png"), defect);
Cv2.ImWrite(Path.Combine(outDir, "out_albedo.png"), albedoImg);

// ── NormalMap 디코드 → 정답과 각도 오차 ──
// 인코딩: enc = (n + 1) * 127.5 (채널 순서 nx,ny,nz)
var nmIdx = normalMap.GetGenericIndexer<Vec3b>();
var errAll = new List<double>();
var errCore = new List<double>();   // 구 중심부(r < 0.8R, 평면/경계 제외)
for (int y = 0; y < N; y++)
    for (int x = 0; x < N; x++)
    {
        var e = nmIdx[y, x];
        double rx = e.Item0 / 127.5 - 1.0;
        double ry = e.Item1 / 127.5 - 1.0;
        double rz = e.Item2 / 127.5 - 1.0;
        double m = Math.Sqrt(rx * rx + ry * ry + rz * rz);
        if (m < 1e-6) continue;
        rx /= m; ry /= m; rz /= m;

        var (gx, gy, gz) = gt[y, x];
        double dot = Math.Clamp(rx * gx + ry * gy + rz * gz, -1, 1);
        double deg = Math.Acos(dot) * 180 / Math.PI;
        errAll.Add(deg);

        double dx = x - cx, dy = y - cy;
        if (dx * dx + dy * dy < (0.8 * R) * (0.8 * R))
            errCore.Add(deg);
    }

static (double mean, double median, double p95) Stats(List<double> v)
{
    if (v.Count == 0) return (0, 0, 0);
    v.Sort();
    double mean = 0; foreach (var d in v) mean += d; mean /= v.Count;
    double median = v[v.Count / 2];
    double p95 = v[(int)(v.Count * 0.95)];
    return (mean, median, p95);
}

var (ma, mea, p95a) = Stats(errAll);
var (mc, mec, p95c) = Stats(errCore);

Console.WriteLine("\n[ 법선 복원 각도 오차 (도) ]");
Console.WriteLine($"  전체 영역   mean={ma,6:F2}  median={mea,6:F2}  p95={p95a,6:F2}  (n={errAll.Count})");
Console.WriteLine($"  구 중심부   mean={mc,6:F2}  median={mec,6:F2}  p95={p95c,6:F2}  (n={errCore.Count})");
Console.WriteLine($"\n출력 이미지: {Path.GetFullPath(outDir)}");

string verdict = mec < 3.0 ? "PASS ✅ (중심부 중앙값 < 3°)" : "CHECK ⚠️ (중심부 오차가 큼)";
Console.WriteLine($"판정: {verdict}");

normalMap.Dispose(); defect.Dispose(); albedoImg.Dispose();
