namespace VMS.WeldTeach.Services;

/// <summary>서브패치 다항식 곡면 피팅 옵션 (그라인딩 명세 S3 — 표면 모델 대안).</summary>
public record SurfaceFitOptions
{
    /// <summary>패치 수용 임계 — 코어 점들의 피팅 RMSE(mm)가 이 값 이하면 세분화를 멈춘다.</summary>
    public double RmseMm { get; init; } = 0.1;

    /// <summary>다항식 차수 (2변수) — 3이면 계수 10개.</summary>
    public int Degree { get; init; } = 3;

    /// <summary>패치 확장 비율 — 이웃 패치와 겹치는 폭(코어 반폭 대비). 경계 불연속을 가중 블렌딩으로 제거.</summary>
    public double OverlapFrac { get; init; } = 0.3;

    /// <summary>쿼드트리 최대 깊이 (4^depth 패치 상한).</summary>
    public int MaxDepth { get; init; } = 6;

    /// <summary>패치당 최소 점수 — 미만이면 차수를 낮춰서라도 수용 (과소결정 방지).</summary>
    public int MinPointsPerPatch { get; init; } = 30;
}

/// <summary>
/// 그라인딩 커버리지의 대안 표면 모델 — 투영 좌표 (a, b, h) 점들을 겹치는 서브패치
/// 다항식으로 피팅한다. 코어 RMSE 가 임계를 넘는 패치는 쿼드트리로 재귀 세분화하고,
/// 평가 시에는 겹침 영역의 패치들을 부드러운 가중치로 블렌딩(partition of unity)해
/// 패치 경계에서도 연속인 h(a,b)와 해석적 기울기(∂h/∂a, ∂h/∂b)를 돌려준다.
/// 장점: 매끄러운 해석 법선(노이즈 저감), 잔차 = 결함 신호. 높이맵과 달리 구멍 위로
/// 외삽할 수 있으므로 <b>유효 마스크는 반드시 별도(높이맵 점유 기반)로 유지</b>해야 한다.
/// </summary>
public class FittedSurface
{
    private readonly List<Patch> _patches;
    private readonly SurfaceFitOptions _opt;

    /// <summary>수용된 리프 패치 수.</summary>
    public int PatchCount => _patches.Count;

    /// <summary>수용 패치들의 최대 코어 RMSE(mm) — 달성 품질 표시용.</summary>
    public double MaxRmseMm { get; }

    private FittedSurface(List<Patch> patches, SurfaceFitOptions opt, double maxRmse)
    {
        _patches = patches;
        _opt = opt;
        MaxRmseMm = maxRmse;
    }

    /// <summary>
    /// (a,b) 에서 블렌딩 평가 — h 와 해석 기울기. 어떤 패치의 확장 영역에도 들지
    /// 않으면 false (호출측은 높이맵 값으로 폴백).
    /// </summary>
    public bool TryEvaluate(double a, double b, out double h, out double ha, out double hb)
    {
        // 가중 합 H = Σ wᵢhᵢ, W = Σ wᵢ → h = H/W. 기울기는 몫의 미분 전체
        // (가중치의 공간 변화 항 포함 — 생략하면 패치 경계에서 법선이 꺾인다).
        double W = 0, H = 0, Wa = 0, Wb = 0, Ha = 0, Hb = 0;
        foreach (var p in _patches)
        {
            if (!p.TryWeight(a, b, out double w, out double wa, out double wb)) continue;
            p.Eval(a, b, out double hi, out double hia, out double hib);
            W += w; H += w * hi;
            Wa += wa; Wb += wb;
            Ha += wa * hi + w * hia;
            Hb += wb * hi + w * hib;
        }
        if (W < 1e-12) { h = ha = hb = 0; return false; }
        h = H / W;
        ha = (Ha * W - H * Wa) / (W * W);
        hb = (Hb * W - H * Wb) / (W * W);
        return true;
    }

    /// <summary>투영 점 (pa, pb, ph) 를 피팅한다 (regionIndices 없이 배열 전체 사용).</summary>
    public static FittedSurface Fit(double[] pa, double[] pb, double[] ph, SurfaceFitOptions? options = null)
    {
        var opt = options ?? new SurfaceFitOptions();
        int n = pa.Length;
        double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            minA = Math.Min(minA, pa[i]); maxA = Math.Max(maxA, pa[i]);
            minB = Math.Min(minB, pb[i]); maxB = Math.Max(maxB, pb[i]);
        }

        var patches = new List<Patch>();
        double maxRmse = 0;
        var all = new int[n];
        for (int i = 0; i < n; i++) all[i] = i;
        FitNode(minA, maxA, minB, maxB, all, 0);
        return new FittedSurface(patches, opt, maxRmse);

        void FitNode(double a0, double a1, double b0, double b1, int[] core, int depth)
        {
            if (core.Length == 0) return;   // 빈 사분면(구멍) — 패치 없음

            // 확장 영역 점 수집 (겹침 — 블렌딩과 경계 조건화의 재료)
            double halfA = (a1 - a0) / 2, halfB = (b1 - b0) / 2;
            double extA0 = a0 - halfA * opt.OverlapFrac, extA1 = a1 + halfA * opt.OverlapFrac;
            double extB0 = b0 - halfB * opt.OverlapFrac, extB1 = b1 + halfB * opt.OverlapFrac;
            var ext = new List<int>(core.Length * 2);
            for (int i = 0; i < n; i++)
                if (pa[i] >= extA0 && pa[i] <= extA1 && pb[i] >= extB0 && pb[i] <= extB1)
                    ext.Add(i);

            // 점수에 맞는 차수 선택 (최소 3×계수 확보 — 과소결정·이상치 민감 방지)
            int degree = opt.Degree;
            while (degree > 0 && ext.Count < CoeffCount(degree) * 3) degree--;

            var patch = Patch.TryFit(pa, pb, ph, ext, a0, a1, b0, b1, extA0, extA1, extB0, extB1, degree);
            if (patch == null)
            {
                // 특이(공선 등) — 평균 높이 상수 패치로 폴백
                patch = Patch.Constant(ph, ext, a0, a1, b0, b1, extA0, extA1, extB0, extB1);
            }

            double rmse = patch.RmseOver(pa, pb, ph, core);
            bool accept = rmse <= opt.RmseMm
                          || depth >= opt.MaxDepth
                          || core.Length < opt.MinPointsPerPatch;
            if (accept)
            {
                patches.Add(patch);
                maxRmse = Math.Max(maxRmse, rmse);
                return;
            }

            // 재귀 세분화 — 4사분면
            double ma = (a0 + a1) / 2, mb = (b0 + b1) / 2;
            var q = new List<int>[4] { new(), new(), new(), new() };
            foreach (var i in core)
                q[(pa[i] >= ma ? 1 : 0) + (pb[i] >= mb ? 2 : 0)].Add(i);
            FitNode(a0, ma, b0, mb, q[0].ToArray(), depth + 1);
            FitNode(ma, a1, b0, mb, q[1].ToArray(), depth + 1);
            FitNode(a0, ma, mb, b1, q[2].ToArray(), depth + 1);
            FitNode(ma, a1, mb, b1, q[3].ToArray(), depth + 1);
        }
    }

    internal static int CoeffCount(int degree) => (degree + 1) * (degree + 2) / 2;

    /// <summary>리프 패치 — 정규화 좌표 다항식 + 확장 영역 기반 블렌딩 가중치.</summary>
    private sealed class Patch
    {
        private double _ca, _cb, _sa, _sb;          // 정규화 (u = (a−ca)/sa)
        private double _extHalfA, _extHalfB;         // 가중치 반폭 (확장 영역)
        private double[] _coef = Array.Empty<double>();
        private int _degree;

        public static Patch? TryFit(double[] pa, double[] pb, double[] ph, List<int> pts,
            double a0, double a1, double b0, double b1,
            double extA0, double extA1, double extB0, double extB1, int degree)
        {
            var p = new Patch
            {
                _ca = (a0 + a1) / 2, _cb = (b0 + b1) / 2,
                _sa = Math.Max((a1 - a0) / 2, 1e-9), _sb = Math.Max((b1 - b0) / 2, 1e-9),
                _extHalfA = Math.Max((extA1 - extA0) / 2, 1e-9),
                _extHalfB = Math.Max((extB1 - extB0) / 2, 1e-9),
                _degree = degree,
            };
            int nc = CoeffCount(degree);

            // 정규방정식 (AᵀA)c = Aᵀy — nc ≤ 10 이라 직접 구성·소거가 가장 싸다
            var ata = new double[nc, nc];
            var aty = new double[nc];
            var terms = new double[nc];
            foreach (var i in pts)
            {
                p.Terms(pa[i], pb[i], terms);
                for (int r = 0; r < nc; r++)
                {
                    aty[r] += terms[r] * ph[i];
                    for (int c = r; c < nc; c++) ata[r, c] += terms[r] * terms[c];
                }
            }
            for (int r = 0; r < nc; r++)
                for (int c = 0; c < r; c++) ata[r, c] = ata[c, r];

            var coef = SolveSymmetric(ata, aty, nc);
            if (coef == null) return null;
            p._coef = coef;
            return p;
        }

        public static Patch Constant(double[] ph, List<int> pts,
            double a0, double a1, double b0, double b1,
            double extA0, double extA1, double extB0, double extB1)
        {
            double mean = 0;
            foreach (var i in pts) mean += ph[i];
            if (pts.Count > 0) mean /= pts.Count;
            return new Patch
            {
                _ca = (a0 + a1) / 2, _cb = (b0 + b1) / 2,
                _sa = Math.Max((a1 - a0) / 2, 1e-9), _sb = Math.Max((b1 - b0) / 2, 1e-9),
                _extHalfA = Math.Max((extA1 - extA0) / 2, 1e-9),
                _extHalfB = Math.Max((extB1 - extB0) / 2, 1e-9),
                _degree = 0,
                _coef = new[] { mean },
            };
        }

        /// <summary>단항 항 값 (정규화 좌표, 차수 오름차순 i+j≤degree).</summary>
        private void Terms(double a, double b, double[] into)
        {
            double u = (a - _ca) / _sa, v = (b - _cb) / _sb;
            int k = 0;
            for (int total = 0; total <= _degree; total++)
                for (int i = total; i >= 0; i--)
                    into[k++] = Math.Pow(u, i) * Math.Pow(v, total - i);
        }

        public void Eval(double a, double b, out double h, out double ha, out double hb)
        {
            double u = (a - _ca) / _sa, v = (b - _cb) / _sb;
            h = 0; double hu = 0, hv = 0;
            int k = 0;
            for (int total = 0; total <= _degree; total++)
                for (int i = total; i >= 0; i--)
                {
                    int j = total - i;
                    double pu = Math.Pow(u, i), pv = Math.Pow(v, j);
                    double c = _coef[k++];
                    h += c * pu * pv;
                    if (i > 0) hu += c * i * Math.Pow(u, i - 1) * pv;
                    if (j > 0) hv += c * pu * j * Math.Pow(v, j - 1);
                }
            ha = hu / _sa;
            hb = hv / _sb;
        }

        public double RmseOver(double[] pa, double[] pb, double[] ph, int[] core)
        {
            if (core.Length == 0) return 0;
            double se = 0;
            foreach (var i in core)
            {
                Eval(pa[i], pb[i], out double h, out _, out _);
                double d = h - ph[i];
                se += d * d;
            }
            return Math.Sqrt(se / core.Length);
        }

        /// <summary>블렌딩 가중치 w = (1−t²)² 곱 (t = 확장 반폭 대비 거리) + 공간 도함수.</summary>
        public bool TryWeight(double a, double b, out double w, out double wa, out double wb)
        {
            double ta = (a - _ca) / _extHalfA, tb = (b - _cb) / _extHalfB;
            if (Math.Abs(ta) >= 1 || Math.Abs(tb) >= 1) { w = wa = wb = 0; return false; }
            double fa = 1 - ta * ta, fb = 1 - tb * tb;
            double gwa = fa * fa, gwb = fb * fb;
            w = gwa * gwb;
            // d/da (1−ta²)² = −4·ta·(1−ta²) / extHalfA
            wa = -4 * ta * fa / _extHalfA * gwb;
            wb = gwa * (-4 * tb * fb / _extHalfB);
            return true;
        }

        /// <summary>대칭 양정치(정규방정식) 가우스 소거 — 특이 시 null.</summary>
        private static double[]? SolveSymmetric(double[,] a, double[] y, int n)
        {
            var m = new double[n, n + 1];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++) m[r, c] = a[r, c];
                m[r, n] = y[r];
            }
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int r = col + 1; r < n; r++)
                    if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
                if (Math.Abs(m[piv, col]) < 1e-12) return null;
                if (piv != col)
                    for (int c = col; c <= n; c++) (m[col, c], m[piv, c]) = (m[piv, c], m[col, c]);
                for (int r = col + 1; r < n; r++)
                {
                    double f = m[r, col] / m[col, col];
                    for (int c = col; c <= n; c++) m[r, c] -= f * m[col, c];
                }
            }
            var x = new double[n];
            for (int r = n - 1; r >= 0; r--)
            {
                double s = m[r, n];
                for (int c = r + 1; c < n; c++) s -= m[r, c] * x[c];
                x[r] = s / m[r, r];
            }
            return x;
        }
    }
}
