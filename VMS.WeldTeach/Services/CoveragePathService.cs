using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;

namespace VMS.WeldTeach.Services;

/// <summary>커버리지 생성 결과 통계. Ambiguous25DCells &gt; 0 이면 2.5D 위반(높이 겹침) 경고.</summary>
/// <param name="FitPatchCount">곡면 피팅 사용 시 리프 패치 수 (미사용 0).</param>
/// <param name="FitRmseMm">곡면 피팅 사용 시 달성 최대 코어 RMSE(mm).</param>
public record CoverageResult(
    List<CoverageScanline> Scanlines,
    double TotalLengthMm,
    double CoveredAreaMm2,
    int LineCount,
    int Ambiguous25DCells,
    int FitPatchCount = 0,
    double FitRmseMm = 0);

/// <summary>
/// 그라인딩 스캔 명세 Step S3 — 2.5D 높이맵 래스터 커버리지 경로 생성.
/// 영역 점군의 PCA 주평면에 투영한 높이맵 그리드에서 스텝오버 간격의 스캔라인을 만든다.
/// 제약: 주평면에 단사 투영 가능한 2.5D 표면만 지원 — 셀 높이 범위가 임계를 넘으면
/// 2.5D 위반으로 집계해 경고한다 (오버행/폐곡면은 범위 외).
/// </summary>
public class CoveragePathService
{
    /// <summary>세그먼트 최소 길이 — 이보다 짧은 조각(노이즈 부스러기)은 버린다.</summary>
    private const double MinSegmentMm = 2.0;

    public CoverageResult Generate(IReadOnlyList<Point3D> cloudPoints,
        IReadOnlyList<Vector3D> cloudNormals, IReadOnlyList<int> regionIndices,
        GrindingParams prm)
    {
        if (regionIndices.Count < 20)
            throw new InvalidOperationException("커버리지 생성에 필요한 영역 점이 부족합니다 (≥20).");
        bool hasNormals = cloudNormals.Count == cloudPoints.Count;
        double cell = Math.Clamp(prm.GridCellMm, 0.2, 10.0);
        double stepover = prm.StepoverMm;

        // ---- 1) PCA 주평면 — e1=래스터 방향(최장축), e2=스텝오버 방향, e3=평면 법선 ----
        var centroid = new Point3D();
        {
            double sx = 0, sy = 0, sz = 0;
            foreach (var i in regionIndices) { var p = cloudPoints[i]; sx += p.X; sy += p.Y; sz += p.Z; }
            centroid = new Point3D(sx / regionIndices.Count, sy / regionIndices.Count, sz / regionIndices.Count);
        }
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var i in regionIndices)
        {
            var d = cloudPoints[i] - centroid;
            xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z;
            yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
        }
        var axes = SymmetricEigenAxes(xx, xy, xz, yy, yz, zz);   // 분산 내림차순
        var e1 = axes[0];
        var e2 = axes[1];
        var e3 = Vector3D.CrossProduct(e1, e2);
        e3.Normalize();

        // 평면 법선을 점 법선 평균 방향으로 정렬 (공구가 표면 바깥쪽을 보도록)
        var nAvg = new Vector3D(0, 0, 1);
        if (hasNormals)
        {
            var s = new Vector3D();
            foreach (var i in regionIndices) s += cloudNormals[i];
            if (s.Length > 1e-9) { s.Normalize(); nAvg = s; }
        }
        if (Vector3D.DotProduct(e3, nAvg) < 0) { e3 = -e3; e2 = -e2; }   // 우수좌표 유지 반전

        // 래스터 방향 회전 (0 = PCA 최장축)
        if (Math.Abs(prm.RasterAngleDeg) > 1e-9)
        {
            double rad = prm.RasterAngleDeg * Math.PI / 180.0;
            double cs = Math.Cos(rad), sn = Math.Sin(rad);
            var r1 = e1 * cs + e2 * sn;
            var r2 = -e1 * sn + e2 * cs;
            e1 = r1; e2 = r2;
        }

        // ---- 2) 주평면 투영 (a=e1, b=e2, h=e3) + 높이맵 그리드 집계 ----
        int n = regionIndices.Count;
        var pa = new double[n]; var pb = new double[n]; var ph = new double[n];
        double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
        for (int k = 0; k < n; k++)
        {
            var d = cloudPoints[regionIndices[k]] - centroid;
            pa[k] = Vector3D.DotProduct(d, e1);
            pb[k] = Vector3D.DotProduct(d, e2);
            ph[k] = Vector3D.DotProduct(d, e3);
            minA = Math.Min(minA, pa[k]); maxA = Math.Max(maxA, pa[k]);
            minB = Math.Min(minB, pb[k]); maxB = Math.Max(maxB, pb[k]);
        }
        // 데이터 범위(스캔라인 배치·샘플링 기준)와 그리드 원점을 분리하고 1셀 패딩 —
        // 닫힘(팽창→침식)의 침식이 배열 경계(밖=false)에서 데이터 최외곽 셀을 깎아
        // 경계 라인이 통째로 사라지는 것을 막는다.
        double dataMinA = minA, dataMaxA = maxA, dataMinB = minB, dataMaxB = maxB;
        minA -= cell; minB -= cell;
        double gridMaxA = dataMaxA + cell, gridMaxB = dataMaxB + cell;
        int na = Math.Max(1, (int)Math.Ceiling((gridMaxA - minA) / cell)) + 1;
        int nb = Math.Max(1, (int)Math.Ceiling((gridMaxB - minB) / cell)) + 1;
        if ((long)na * nb > 4_000_000)
            throw new InvalidOperationException($"높이맵 그리드가 너무 큽니다 ({na}×{nb}) — 셀 크기를 키우세요.");

        var cnt = new int[na, nb];
        var hSum = new double[na, nb];
        var hMin = new double[na, nb];
        var hMax = new double[na, nb];
        var nSum = new Vector3D[na, nb];
        for (int ia = 0; ia < na; ia++)
            for (int ib = 0; ib < nb; ib++) { hMin[ia, ib] = double.MaxValue; hMax[ia, ib] = double.MinValue; }

        for (int k = 0; k < n; k++)
        {
            int ia = Math.Clamp((int)((pa[k] - minA) / cell), 0, na - 1);
            int ib = Math.Clamp((int)((pb[k] - minB) / cell), 0, nb - 1);
            cnt[ia, ib]++;
            hSum[ia, ib] += ph[k];
            hMin[ia, ib] = Math.Min(hMin[ia, ib], ph[k]);
            hMax[ia, ib] = Math.Max(hMax[ia, ib], ph[k]);
            if (hasNormals) nSum[ia, ib] += cloudNormals[regionIndices[k]];
        }

        // 2.5D 위반 — 한 셀에 상·하면이 겹치면 높이 범위가 튄다 (임계: max(4셀, 4mm))
        double ambThresh = Math.Max(4 * cell, 4.0);
        int ambiguous = 0;
        var occupied = new bool[na, nb];
        for (int ia = 0; ia < na; ia++)
            for (int ib = 0; ib < nb; ib++)
                if (cnt[ia, ib] > 0)
                {
                    occupied[ia, ib] = true;
                    if (hMax[ia, ib] - hMin[ia, ib] > ambThresh) ambiguous++;
                }

        // ---- 3) 유효 마스크 — 모폴로지 닫힘(1셀)으로 작은 틈 메우고, 마진은 침식으로 ----
        var mask = Erode(Dilate(occupied, na, nb), na, nb);
        int marginCells = (int)Math.Round(prm.MarginMm / cell);
        for (int e = 0; e < marginCells; e++) mask = Erode(mask, na, nb);

        // 닫힘으로 생긴 데이터 없는 유효 셀 — 이웃 평균으로 높이·법선 보충
        var hVal = new double[na, nb];
        var nVal = new Vector3D[na, nb];
        var hasData = new bool[na, nb];
        for (int ia = 0; ia < na; ia++)
            for (int ib = 0; ib < nb; ib++)
                if (occupied[ia, ib])
                {
                    hVal[ia, ib] = hSum[ia, ib] / cnt[ia, ib];
                    var nv = hasNormals && nSum[ia, ib].Length > 1e-9 ? nSum[ia, ib] : e3;
                    nv.Normalize();
                    nVal[ia, ib] = nv;
                    hasData[ia, ib] = true;
                }
        for (int ia = 0; ia < na; ia++)
            for (int ib = 0; ib < nb; ib++)
            {
                if (!mask[ia, ib] || hasData[ia, ib]) continue;
                double hs = 0; var ns = new Vector3D(); int c = 0;
                for (int da = -1; da <= 1; da++)
                    for (int db = -1; db <= 1; db++)
                    {
                        int ja = ia + da, jb = ib + db;
                        if (ja < 0 || ja >= na || jb < 0 || jb >= nb || !occupied[ja, jb]) continue;
                        hs += hVal[ja, jb]; ns += nVal[ja, jb]; c++;
                    }
                if (c > 0)
                {
                    hVal[ia, ib] = hs / c;
                    if (ns.Length > 1e-9) ns.Normalize(); else ns = e3;
                    nVal[ia, ib] = ns;
                    hasData[ia, ib] = true;
                }
                else mask[ia, ib] = false;   // 보충 불가 — 무효 처리
            }

        int maskCells = 0;
        for (int ia = 0; ia < na; ia++)
            for (int ib = 0; ib < nb; ib++)
                if (mask[ia, ib]) maskCells++;

        // ---- 3.5) 표면 모델 옵션 — 서브패치 다항식 피팅 ----
        // 높이·법선 샘플 원천을 그리드 쌍선형 → 피팅 곡면(해석 기울기)으로 교체한다.
        // 유효 마스크·2.5D 위반은 여전히 그리드 점유 기반 — 피팅은 구멍 위로도
        // 매끄럽게 외삽하므로 마스크 없이 쓰면 구멍이 사라진다.
        FittedSurface? fitted = null;
        if (prm.UseSurfaceFit)
            fitted = FittedSurface.Fit(pa, pb, ph,
                new SurfaceFitOptions { RmseMm = Math.Clamp(prm.FitRmseMm, 0.005, 5.0) });

        // ---- 4) 스캔라인 배치 (b 방향 스텝오버, 표면 호길이 기준) + 샘플링 → 세그먼트 분할 ----
        // 스텝오버 3D 보정 — 주평면상 등간격은 경사·곡면에서 실제 표면 간격이 벌어져
        // 미연마 띠가 남는다 (원통 셸 40° 가장자리에서 최대 1.31배). b 를 셀 간격으로
        // 훑으며 각 밴드의 3D 신장률을 누적해 호길이 좌표를 만들고, 스텝오버를 그
        // 좌표에서 등간격으로 배치한다 (평면이면 신장률 1 — 기존 동작과 동일).
        // 밴드 신장률은 a 전체의 최대 높이차 기준 — 국부 급경사에서 과커버 쪽으로
        // 치우치게 한다 (명세 §4: 누락 연마 방지가 과커버보다 우선).
        int bSteps = Math.Max(1, (int)Math.Ceiling((dataMaxB - dataMinB) / cell));
        double bStep = (dataMaxB - dataMinB) / bSteps;
        var arc = new double[bSteps + 1];
        for (int j = 0; j < bSteps; j++)
        {
            double b0 = dataMinB + j * bStep, b1 = b0 + bStep;
            double maxDh = 0;
            for (double a = dataMinA; a <= dataMaxA + 1e-9; a += cell)
                if (TrySample(a, b0, out double h0, out _) && TrySample(a, b1, out double h1, out _))
                    maxDh = Math.Max(maxDh, Math.Abs(h1 - h0));
            arc[j + 1] = arc[j] + Math.Sqrt(bStep * bStep + maxDh * maxDh);
        }
        double arcTotal = arc[bSteps];

        // 반 스텝 인셋 배치 — 경계(호길이 0/전체) 위 라인은 불규칙 영역에서 접선 슬리버만
        // 남는다. 첫/끝 라인을 경계에서 s/2 안쪽에 두면 공구 반경(D/2 ≥ s/2, 겹침률>0)이
        // 경계를 덮는다.
        double halfStep = stepover / 2;
        var tStations = new List<double>();
        if (arcTotal <= stepover)
            tStations.Add(arcTotal / 2);   // 좁은 영역 — 중앙 한 줄
        else
        {
            for (double t = halfStep; t <= arcTotal - halfStep + 1e-9; t += stepover)
                tStations.Add(t);
            double lastPossible = arcTotal - halfStep;
            if (lastPossible - tStations[^1] >= stepover * 0.3) tStations.Add(lastPossible);
        }
        var bStations = tStations.Select(BFromArc).ToList();

        var scanlines = new List<CoverageScanline>();
        double totalLen = 0;
        int lineCount = 0;
        for (int li = 0; li < bStations.Count; li++)
        {
            double b = bStations[li];
            var lineSegs = new List<CoverageScanline>();
            CoverageScanline? cur = null;

            for (double a = dataMinA; a <= dataMaxA + 1e-9; a += cell)
            {
                bool ok = TrySample(a, b, out double h, out Vector3D nrm);
                if (ok)
                {
                    cur ??= new CoverageScanline { LineIndex = li, SegmentIndex = lineSegs.Count };
                    cur.PathPoints.Add(centroid + a * e1 + b * e2 + h * e3);
                    cur.PointNormals.Add(nrm);
                }
                else if (cur != null) { CloseSegment(lineSegs, cur); cur = null; }
            }
            if (cur != null) CloseSegment(lineSegs, cur);

            if (lineSegs.Count == 0) continue;
            lineCount++;

            // 지그재그 — 홀수 라인은 세그먼트 순서·점 순서 모두 역전
            if (prm.Zigzag && li % 2 == 1)
            {
                lineSegs.Reverse();
                for (int si = 0; si < lineSegs.Count; si++)
                {
                    lineSegs[si].SegmentIndex = si;
                    lineSegs[si].PathPoints.Reverse();
                    lineSegs[si].PointNormals.Reverse();
                    lineSegs[si].Reversed = true;
                }
            }
            foreach (var s in lineSegs) { totalLen += s.LengthMm; scanlines.Add(s); }
        }

        return new CoverageResult(scanlines, totalLen, maskCells * cell * cell, lineCount, ambiguous,
            fitted?.PatchCount ?? 0, fitted?.MaxRmseMm ?? 0);

        // ---- 로컬 함수 ----

        // 표면 호길이 t → b 좌표 (누적 배열 선형 역보간)
        double BFromArc(double t)
        {
            if (t <= 0) return dataMinB;
            if (t >= arcTotal) return dataMaxB;
            int lo = 0, hi = bSteps;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) / 2;
                if (arc[mid] <= t) lo = mid; else hi = mid;
            }
            double seg = arc[lo + 1] - arc[lo];
            double f = seg > 1e-12 ? (t - arc[lo]) / seg : 0;
            return dataMinB + (lo + f) * bStep;
        }

        // (a,b) 샘플 — 마스크 유효 셀에서 셀 중심 쌍선형 보간 (무효 이웃은 가중 제외).
        // 곡면 피팅 모드면 높이·법선을 피팅 곡면에서 얻는다 (마스크 판정은 동일).
        bool TrySample(double a, double b, out double h, out Vector3D nrm)
        {
            h = 0; nrm = e3;
            int ia = (int)((a - minA) / cell), ib = (int)((b - minB) / cell);
            if (ia < 0 || ia >= na || ib < 0 || ib >= nb || !mask[ia, ib]) return false;

            if (fitted != null && fitted.TryEvaluate(a, b, out double fh, out double fha, out double fhb))
            {
                // 곡면 S(a,b) = C + a·e1 + b·e2 + h·e3 의 법선 = −h_a·e1 − h_b·e2 + e3
                h = fh;
                var fn = -fha * e1 - fhb * e2 + e3;
                fn.Normalize();
                nrm = fn;
                return true;
            }

            double u = (a - minA) / cell - 0.5, v = (b - minB) / cell - 0.5;
            int i0 = (int)Math.Floor(u), j0 = (int)Math.Floor(v);
            double fu = u - i0, fv = v - j0;
            double wSum = 0, hAcc = 0;
            var nAcc = new Vector3D();
            for (int di = 0; di <= 1; di++)
                for (int dj = 0; dj <= 1; dj++)
                {
                    int ci = i0 + di, cj = j0 + dj;
                    if (ci < 0 || ci >= na || cj < 0 || cj >= nb || !hasData[ci, cj]) continue;
                    double w = (di == 0 ? 1 - fu : fu) * (dj == 0 ? 1 - fv : fv);
                    if (w <= 0) continue;
                    wSum += w;
                    hAcc += w * hVal[ci, cj];
                    nAcc += w * nVal[ci, cj];
                }
            if (wSum < 1e-9) return false;
            h = hAcc / wSum;
            if (nAcc.Length > 1e-9) { nAcc.Normalize(); nrm = nAcc; }
            return true;
        }

        void CloseSegment(List<CoverageScanline> segs, CoverageScanline seg)
        {
            SmoothHeights(seg);
            double len = 0;
            for (int i = 1; i < seg.PathPoints.Count; i++)
                len += (seg.PathPoints[i] - seg.PathPoints[i - 1]).Length;
            seg.LengthMm = len;
            if (seg.PathPoints.Count >= 3 && len >= MinSegmentMm) segs.Add(seg);
        }

        // 이동평균(창 3) 평활 — 그리드 계단·노이즈 완화 (양 끝점은 유지)
        void SmoothHeights(CoverageScanline seg)
        {
            var p = seg.PathPoints;
            if (p.Count < 3) return;
            var smoothed = new List<Point3D>(p.Count) { p[0] };
            for (int i = 1; i < p.Count - 1; i++)
                smoothed.Add(new Point3D(
                    (p[i - 1].X + p[i].X + p[i + 1].X) / 3,
                    (p[i - 1].Y + p[i].Y + p[i + 1].Y) / 3,
                    (p[i - 1].Z + p[i].Z + p[i + 1].Z) / 3));
            smoothed.Add(p[^1]);
            seg.PathPoints = smoothed;
        }
    }

    private static bool[,] Dilate(bool[,] src, int na, int nb)
    {
        var dst = new bool[na, nb];
        for (int ia = 0; ia < na; ia++)
            for (int ib = 0; ib < nb; ib++)
            {
                if (src[ia, ib]) { dst[ia, ib] = true; continue; }
                for (int da = -1; da <= 1 && !dst[ia, ib]; da++)
                    for (int db = -1; db <= 1; db++)
                    {
                        int ja = ia + da, jb = ib + db;
                        if (ja >= 0 && ja < na && jb >= 0 && jb < nb && src[ja, jb]) { dst[ia, ib] = true; break; }
                    }
            }
        return dst;
    }

    private static bool[,] Erode(bool[,] src, int na, int nb)
    {
        var dst = new bool[na, nb];
        for (int ia = 0; ia < na; ia++)
            for (int ib = 0; ib < nb; ib++)
            {
                if (!src[ia, ib]) continue;
                bool all = true;
                for (int da = -1; da <= 1 && all; da++)
                    for (int db = -1; db <= 1; db++)
                    {
                        int ja = ia + da, jb = ib + db;
                        if (ja < 0 || ja >= na || jb < 0 || jb >= nb || !src[ja, jb]) { all = false; break; }
                    }
                dst[ia, ib] = all;
            }
        return dst;
    }

    /// <summary>대칭 3×3 공분산의 고유벡터 3개 — 고유값 내림차순 (야코비 회전법).</summary>
    private static Vector3D[] SymmetricEigenAxes(
        double xx, double xy, double xz, double yy, double yz, double zz)
    {
        var a = new double[3, 3] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };
        var v = new double[3, 3];
        for (int i = 0; i < 3; i++) v[i, i] = 1;

        for (int sweep = 0; sweep < 50; sweep++)
        {
            int p = 0, q = 1;
            double max = 0;
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    if (Math.Abs(a[i, j]) > max) { max = Math.Abs(a[i, j]); p = i; q = j; }
            if (max < 1e-12) break;

            double theta = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
            double c = Math.Cos(theta), s = Math.Sin(theta);
            for (int i = 0; i < 3; i++)
            {
                double aip = a[i, p], aiq = a[i, q];
                a[i, p] = c * aip - s * aiq;
                a[i, q] = s * aip + c * aiq;
            }
            for (int j = 0; j < 3; j++)
            {
                double apj = a[p, j], aqj = a[q, j];
                a[p, j] = c * apj - s * aqj;
                a[q, j] = s * apj + c * aqj;
            }
            for (int i = 0; i < 3; i++)
            {
                double vip = v[i, p], viq = v[i, q];
                v[i, p] = c * vip - s * viq;
                v[i, q] = s * vip + c * viq;
            }
        }

        var order = new[] { 0, 1, 2 }.OrderByDescending(i => a[i, i]).ToArray();
        var axes = new Vector3D[3];
        for (int k = 0; k < 3; k++)
        {
            var vec = new Vector3D(v[0, order[k]], v[1, order[k]], v[2, order[k]]);
            if (vec.Length < 1e-9) vec = k == 0 ? new Vector3D(1, 0, 0) : k == 1 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
            vec.Normalize();
            axes[k] = vec;
        }
        return axes;
    }
}

