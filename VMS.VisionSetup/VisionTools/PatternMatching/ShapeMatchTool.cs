using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.PatternMatching
{
    /// <summary>
    /// 형상 기반 패턴 매칭 도구 (Cognex PatMax 대응 Phase 1).
    /// NCC(정규화 상관) + 회전/스케일 분기 매칭 — FeatureMatch의 SIFT/SURF 한계(저대비/균일 표면)를 보완.
    /// ROI에서 그레이 템플릿을 학습하고, Execute 시 회전/스케일 그리드를 탐색해 최고 점수 매칭을 반환.
    /// 다중 인스턴스, GHT(엣지 기반) 모드는 Phase 2에서.
    /// </summary>
    public class ShapeMatchTool : VisionToolBase, ISearchRegionTool
    {
        // ── 학습된 템플릿 (PNG 인코딩으로 Recipe 직렬화 친화) ──
        private byte[]? _templatePngBytes;
        public byte[]? TemplatePngBytes
        {
            get => _templatePngBytes;
            set
            {
                if (SetProperty(ref _templatePngBytes, value))
                {
                    _cachedTemplate?.Dispose();
                    _cachedTemplate = null;
                    DisposeRotationCache();
                    OnPropertyChanged(nameof(IsTrained));
                    OnPropertyChanged(nameof(TemplateWidth));
                    OnPropertyChanged(nameof(TemplateHeight));
                }
            }
        }

        private Mat? _cachedTemplate;

        // ── 사전 회전 캐시 (Train 직후 또는 첫 Execute에서 빌드) ──
        // 모든 (각도, 스케일, 피라미드 레벨) 조합의 회전+리사이즈된 Mat을 보관.
        // Execute에서 매번 회전/리사이즈하는 비용을 제거.
        // 파라미터(AngleStep, MinScale, MaxScale, ScaleStep, NumPyramidLevels) 변경 또는
        // 템플릿 교체 시 자동 무효화.
        private Dictionary<(int angleDeci, int scaleMilli, int pyrLevel), Mat>? _rotationCache;
        private CacheSpec? _cachedSpec;

        private readonly struct CacheSpec : IEquatable<CacheSpec>
        {
            public readonly double AngleStep, MinScale, MaxScale, ScaleStep;
            public readonly int NumPyramidLevels, TemplateHash;
            public CacheSpec(double a, double mn, double mx, double s, int p, int h)
            { AngleStep = a; MinScale = mn; MaxScale = mx; ScaleStep = s; NumPyramidLevels = p; TemplateHash = h; }
            public bool Equals(CacheSpec o) => AngleStep == o.AngleStep && MinScale == o.MinScale
                && MaxScale == o.MaxScale && ScaleStep == o.ScaleStep
                && NumPyramidLevels == o.NumPyramidLevels && TemplateHash == o.TemplateHash;
            public override bool Equals(object? obj) => obj is CacheSpec o && Equals(o);
            public override int GetHashCode() => HashCode.Combine(AngleStep, MinScale, MaxScale, ScaleStep, NumPyramidLevels, TemplateHash);
        }

        public bool IsTrained => TemplatePngBytes != null && TemplatePngBytes.Length > 0;
        public int TemplateWidth => GetCachedTemplate()?.Width ?? 0;
        public int TemplateHeight => GetCachedTemplate()?.Height ?? 0;

        // ── 검색 범위 ──
        // StartAngle/EndAngle은 고정 (-180 ~ +180). 사용자가 좁히면 회전 매칭이 다른 형상에 잘못 일치하는 문제 방지.
        public double StartAngle => -180;
        public double EndAngle => 180;

        private double _angleStep = 5;
        public double AngleStep { get => _angleStep; set => SetProperty(ref _angleStep, Math.Max(0.5, value)); }

        private double _minScale = 0.8;
        public double MinScale { get => _minScale; set => SetProperty(ref _minScale, Math.Max(0.1, value)); }

        private double _maxScale = 1.2;
        public double MaxScale { get => _maxScale; set => SetProperty(ref _maxScale, Math.Max(_minScale, value)); }

        private double _scaleStep = 0.1;
        public double ScaleStep { get => _scaleStep; set => SetProperty(ref _scaleStep, Math.Max(0.01, value)); }

        // ── 임계값 / 옵션 ──
        private double _scoreThreshold = 0.7;
        public double ScoreThreshold
        {
            get => _scoreThreshold;
            set => SetProperty(ref _scoreThreshold, Math.Clamp(value, 0.0, 1.0));
        }

        // ── 다중 인스턴스 검출 (NMS) ──
        private int _maxInstances = 1;
        /// <summary>찾을 최대 인스턴스 수. 1이면 단일 매칭 (기존 동작), 2+면 NMS로 중복 제거 후 상위 N개.</summary>
        public int MaxInstances
        {
            get => _maxInstances;
            set => SetProperty(ref _maxInstances, Math.Clamp(value, 1, 50));
        }

        private double _nmsDistanceFactor = 0.5;
        /// <summary>NMS 중심 거리 임계. 매칭 박스 짧은 변 × 이 값보다 가까우면 중복으로 간주하고 낮은 점수 억제.
        /// • 0.3: 매우 좁게(매칭 박스가 거의 겹쳐도 살림)
        /// • 0.5: 기본
        /// • 1.0+: 박스 크기만큼 떨어져야 별개 인스턴스</summary>
        public double NmsDistanceFactor
        {
            get => _nmsDistanceFactor;
            set => SetProperty(ref _nmsDistanceFactor, Math.Clamp(value, 0.1, 3.0));
        }

        // ── 피라미드 + Coarse-to-Fine 가속 ──
        // 거친 레벨에서 모든 (각도, 스케일) 후보를 빠르게 평가 → Top-N 후보만 풀 해상도에서 정밀화.
        // PatMax 정신과 동일.

        private int _numPyramidLevels = 2;
        public int NumPyramidLevels
        {
            get => _numPyramidLevels;
            set => SetProperty(ref _numPyramidLevels, Math.Clamp(value, 1, 4));
        }

        // CoarseAngleStepMultiplier도 고정 (4). 변경 시 결과 품질이 들쭉날쭉하여 튜닝 부담 증가.
        public int CoarseAngleStepMultiplier => 4;

        private int _topCandidates = 3;
        public int TopCandidates
        {
            get => _topCandidates;
            set => SetProperty(ref _topCandidates, Math.Clamp(value, 1, 10));
        }

        // ── Search Region (학습 영역과 분리) ──
        // 의미:
        //   UseROI / ROI*       → Training Region  (TrainFromImage가 사용)
        //   UseSearchRegion / SearchRegion* → Search Region (Execute가 사용)
        // FeatureMatchTool과 동일 컨셉.

        private Rect _searchRegion;
        public Rect SearchRegion
        {
            get => _searchRegion;
            set
            {
                if (SetProperty(ref _searchRegion, value))
                {
                    // 사용자가 SearchRegion을 수동 변경하면 fixture base를 새 기준으로 리셋
                    if (!IsFixtureTransformActive)
                        HasFixtureBaseSearchRegion = false;
                    OnPropertyChanged(nameof(SearchRegionX));
                    OnPropertyChanged(nameof(SearchRegionY));
                    OnPropertyChanged(nameof(SearchRegionWidth));
                    OnPropertyChanged(nameof(SearchRegionHeight));
                }
            }
        }
        public int SearchRegionX
        {
            get => _searchRegion.X;
            set { SearchRegion = new Rect(value, _searchRegion.Y, _searchRegion.Width, _searchRegion.Height); }
        }
        public int SearchRegionY
        {
            get => _searchRegion.Y;
            set { SearchRegion = new Rect(_searchRegion.X, value, _searchRegion.Width, _searchRegion.Height); }
        }
        public int SearchRegionWidth
        {
            get => _searchRegion.Width;
            set { SearchRegion = new Rect(_searchRegion.X, _searchRegion.Y, value, _searchRegion.Height); }
        }
        public int SearchRegionHeight
        {
            get => _searchRegion.Height;
            set { SearchRegion = new Rect(_searchRegion.X, _searchRegion.Y, _searchRegion.Width, value); }
        }

        private bool _useSearchRegion;
        public bool UseSearchRegion
        {
            get => _useSearchRegion;
            set => SetProperty(ref _useSearchRegion, value);
        }

        // FeatureMatchTool과 대칭 — 캔버스에 표시된 SearchRegion ROIShape 참조 (도구 전환 시 복원용)
        private ROIShape? _associatedSearchRegionShape;
        public ROIShape? AssociatedSearchRegionShape
        {
            get => _associatedSearchRegionShape;
            set => SetProperty(ref _associatedSearchRegionShape, value);
        }

        public ShapeMatchTool()
        {
            Name = "Shape Match";
            ToolType = "ShapeMatchTool";
        }

        /// <summary>
        /// 현재 ROI(또는 전체 이미지)를 그레이 템플릿으로 학습.
        /// 호출자가 VisionService.CurrentImage를 src로 전달.
        /// </summary>
        public bool TrainFromImage(Mat src)
        {
            if (src == null || src.Empty()) return false;

            Mat work;
            if (UseROI)
            {
                var adj = GetAdjustedROI(src);
                if (adj.Width <= 4 || adj.Height <= 4) return false;
                work = new Mat(src, adj);
            }
            else
            {
                work = src.Clone();
            }

            try
            {
                using var gray = work.Channels() > 1
                    ? work.CvtColor(ColorConversionCodes.BGR2GRAY)
                    : work.Clone();
                Cv2.ImEncode(".png", gray, out var bytes);
                TemplatePngBytes = bytes;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (work != src) work.Dispose();
            }
        }

        private Mat? GetCachedTemplate()
        {
            if (_cachedTemplate != null && !_cachedTemplate.IsDisposed) return _cachedTemplate;
            if (TemplatePngBytes == null || TemplatePngBytes.Length == 0) return null;
            try
            {
                _cachedTemplate = Cv2.ImDecode(TemplatePngBytes, ImreadModes.Grayscale);
                return _cachedTemplate;
            }
            catch
            {
                return null;
            }
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                var template = GetCachedTemplate();
                if (template == null || template.Empty())
                {
                    result.Success = false;
                    result.Message = "Template not trained. Use 'Train Template' in settings.";
                    return result;
                }

                // Search Region 적용 (UseROI는 Training 전용, Execute는 SearchRegion 사용)
                Rect searchRect;
                Mat workArea;
                if (UseSearchRegion && SearchRegion.Width > 0 && SearchRegion.Height > 0)
                {
                    searchRect = ClipRect(SearchRegion, inputImage.Width, inputImage.Height);
                    if (searchRect.Width <= 0 || searchRect.Height <= 0)
                    {
                        result.Success = false;
                        result.Message = "Search region is outside the image.";
                        return result;
                    }
                    workArea = new Mat(inputImage, searchRect);
                }
                else
                {
                    searchRect = new Rect(0, 0, inputImage.Width, inputImage.Height);
                    workArea = inputImage.Clone();
                }

                using var roi = workArea;
                using var gray = roi.Channels() > 1
                    ? roi.CvtColor(ColorConversionCodes.BGR2GRAY)
                    : roi.Clone();

                // 피라미드 + Coarse-to-Fine + NMS 다중 인스턴스 탐색
                var matches = RunPyramidSearch(gray, template, out int gridCount);
                double offsetX = searchRect.X;
                double offsetY = searchRect.Y;
                bool passed = matches.Count > 0;

                // 1순위 매칭은 기존 키와 호환 유지 (단일 모드)
                if (matches.Count > 0)
                {
                    var top = matches[0];
                    double cx = offsetX + top.Loc.X + top.Size.Width / 2.0;
                    double cy = offsetY + top.Loc.Y + top.Size.Height / 2.0;
                    result.Data["Score"] = top.Score;
                    result.Data["CenterX"] = cx;
                    result.Data["CenterY"] = cy;
                    result.Data["Angle"] = top.Angle;
                    result.Data["Scale"] = top.Scale;
                    result.Data["MatchWidth"] = top.Size.Width;
                    result.Data["MatchHeight"] = top.Size.Height;
                }
                result.Data["MatchCount"] = matches.Count;
                result.Data["GridEvaluations"] = gridCount;

                // 모든 매칭을 키 별로
                for (int i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];
                    double cx = offsetX + m.Loc.X + m.Size.Width / 2.0;
                    double cy = offsetY + m.Loc.Y + m.Size.Height / 2.0;
                    result.Data[$"Match{i}_Score"] = m.Score;
                    result.Data[$"Match{i}_CenterX"] = cx;
                    result.Data[$"Match{i}_CenterY"] = cy;
                    result.Data[$"Match{i}_Angle"] = m.Angle;
                    result.Data[$"Match{i}_Scale"] = m.Scale;
                    result.Data[$"Match{i}_Width"] = m.Size.Width;
                    result.Data[$"Match{i}_Height"] = m.Size.Height;
                }

                // 오버레이
                var overlay = GetColorOverlayBase(inputImage);

                // Search Region 시각화 (활성 시) — 노란 직사각형
                if (UseSearchRegion && searchRect.Width > 0 && searchRect.Height > 0)
                {
                    Cv2.Rectangle(overlay,
                        new Point(searchRect.X, searchRect.Y),
                        new Point(searchRect.X + searchRect.Width, searchRect.Y + searchRect.Height),
                        new Scalar(0, 255, 255), 2);
                    Cv2.PutText(overlay, "Search Region",
                        new Point(searchRect.X + 4, searchRect.Y + 16),
                        HersheyFonts.HersheySimplex, 0.45, new Scalar(0, 255, 255), 1);
                }

                // 모든 매칭 인스턴스 그리기 (1부터 번호)
                for (int i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];
                    double cx = offsetX + m.Loc.X + m.Size.Width / 2.0;
                    double cy = offsetY + m.Loc.Y + m.Size.Height / 2.0;
                    var color = new Scalar(0, 255, 0);
                    DrawRotatedBox(overlay, cx, cy, m.Size.Width, m.Size.Height, m.Angle, color);
                    Cv2.DrawMarker(overlay, new Point((int)cx, (int)cy), color, MarkerTypes.Cross, 18, 2);
                    Cv2.PutText(overlay,
                        $"#{i + 1} S={m.Score:F2}",
                        new Point((int)cx + 10, (int)cy - 10),
                        HersheyFonts.HersheySimplex, 0.5, color, 1);
                }

                result.OutputImage = overlay.Clone();
                result.OverlayImage = overlay;
                result.Success = passed;
                result.Message = passed
                    ? (matches.Count == 1
                        ? $"Match: score={matches[0].Score:F3}, angle={matches[0].Angle:F1}°, scale={matches[0].Scale:F2}"
                        : $"Found {matches.Count} instances (top score={matches[0].Score:F3})")
                    : $"No match above threshold ({ScoreThreshold:F2}).";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Shape Match failed: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        // ─── 회전 캐시 ───────────────────────────────────────────

        /// <summary>
        /// 현재 파라미터 / 템플릿 기준으로 캐시가 유효한지 검사하고, 필요 시 재빌드.
        /// 첫 호출 또는 파라미터 변경 후 호출 시 시간 비용 발생 (한 번만).
        /// </summary>
        private void EnsureRotationCache(Mat template)
        {
            int hash = QuickTemplateHash(TemplatePngBytes);
            var spec = new CacheSpec(AngleStep, MinScale, MaxScale, ScaleStep, NumPyramidLevels, hash);
            if (_rotationCache != null && _cachedSpec.HasValue && _cachedSpec.Value.Equals(spec)) return;

            DisposeRotationCache();
            _rotationCache = new Dictionary<(int, int, int), Mat>();

            var pyramid = BuildPyramid(template, NumPyramidLevels);
            try
            {
                for (int p = 0; p < pyramid.Count; p++)
                {
                    var srcLevel = pyramid[p];
                    for (double a = -180; a <= 180 + 1e-6; a += AngleStep)
                    {
                        using var rotated = RotateAround(srcLevel, a);
                        for (double s = MinScale; s <= MaxScale + 1e-6; s += ScaleStep)
                        {
                            int nw = (int)Math.Round(rotated.Width * s);
                            int nh = (int)Math.Round(rotated.Height * s);
                            if (nw < 4 || nh < 4) continue;

                            var scaled = new Mat();
                            Cv2.Resize(rotated, scaled, new Size(nw, nh), 0, 0,
                                s < 1 ? InterpolationFlags.Area : InterpolationFlags.Cubic);
                            _rotationCache[CacheKey(a, s, p)] = scaled;
                        }
                    }
                }
            }
            finally
            {
                foreach (var m in pyramid) m.Dispose();
            }
            _cachedSpec = spec;
        }

        private static (int, int, int) CacheKey(double angle, double scale, int pyrLevel)
            => ((int)Math.Round(angle * 10), (int)Math.Round(scale * 1000), pyrLevel);

        /// <summary>
        /// 템플릿 바이트의 빠른 해시 (길이 + 앞/뒤 몇 바이트). 충돌이 사실상 0이고 O(1).
        /// 캐시 무효화 판정용이라 암호학적 해시 불필요.
        /// </summary>
        private static int QuickTemplateHash(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return 0;
            int n = bytes.Length;
            int h = n;
            // 길이와 양 끝의 몇 바이트만 섞음
            for (int i = 0; i < Math.Min(8, n); i++)
                h = (h * 31) ^ bytes[i];
            for (int i = Math.Max(0, n - 8); i < n; i++)
                h = (h * 31) ^ bytes[i];
            return h;
        }

        /// <summary>
        /// 캐시에서 (각도, 스케일, 피라미드 레벨) 조합의 회전+리사이즈 Mat을 조회.
        /// 캐시 miss 시 null (호출자가 fallback으로 즉석 회전).
        /// </summary>
        private Mat? TryGetCached(double angle, double scale, int pyrLevel)
        {
            if (_rotationCache == null) return null;
            return _rotationCache.TryGetValue(CacheKey(angle, scale, pyrLevel), out var m) ? m : null;
        }

        private void DisposeRotationCache()
        {
            if (_rotationCache == null) return;
            foreach (var m in _rotationCache.Values) m?.Dispose();
            _rotationCache.Clear();
            _rotationCache = null;
            _cachedSpec = null;
        }

        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// 다중 해상도 피라미드 + Coarse-to-Fine 탐색.
        /// 1) 가장 거친 레벨에서 큰 각도/스케일 스텝으로 모든 후보 평가 → Top-N
        /// 2) 풀 해상도에서 각 후보 주변만 정밀화
        /// PatMax 정신과 동일한 방식.
        /// </summary>
        /// <summary>다중 인스턴스 매칭 결과 (NMS 적용 후).</summary>
        internal readonly struct MatchInstance
        {
            public readonly double Score, Angle, Scale;
            public readonly Point Loc;
            public readonly Size Size;
            public MatchInstance(double s, double a, double sc, Point l, Size sz)
            { Score = s; Angle = a; Scale = sc; Loc = l; Size = sz; }
        }

        private List<MatchInstance> RunPyramidSearch(Mat gray, Mat template, out int evalCount)
        {
            evalCount = 0;

            // 회전 캐시 빌드 (필요 시) — 모든 (각도, 스케일, 피라미드 레벨) 사전 회전+리사이즈
            EnsureRotationCache(template);

            var grayLevels = BuildPyramid(gray, NumPyramidLevels);
            try
            {
                int topIdx = grayLevels.Count - 1;
                int pyrFactor = 1 << topIdx;

                // ── Coarse pass ──
                var coarseGray = grayLevels[topIdx];
                double coarseStep = AngleStep * CoarseAngleStepMultiplier;
                double aStart = Math.Min(StartAngle, EndAngle);
                double aEnd = Math.Max(StartAngle, EndAngle);

                // 다중 인스턴스를 위해 후보를 충분히 많이 수집.
                // 단일 모드(MaxInstances=1)는 TopCandidates로 충분, 다중 모드는 MaxInstances × 3 이상.
                int candidatePoolSize = Math.Max(TopCandidates, MaxInstances * 3);
                var candidates = new List<(double score, double angle, double scale, Point locFull, Size size)>();

                for (double a = aStart; a <= aEnd + 1e-6; a += coarseStep)
                {
                    for (double s = MinScale; s <= MaxScale + 1e-6; s += ScaleStep)
                    {
                        var cached = TryGetCached(a, s, topIdx);
                        if (cached == null) continue;
                        if (cached.Width < 4 || cached.Height < 4) continue;
                        if (cached.Width > coarseGray.Width || cached.Height > coarseGray.Height) continue;

                        // 다중 인스턴스 모드에서는 한 매칭맵에서 여러 로컬 피크를 채집.
                        using var map = new Mat();
                        Cv2.MatchTemplate(coarseGray, cached, map, TemplateMatchModes.CCoeffNormed);

                        if (MaxInstances <= 1)
                        {
                            Cv2.MinMaxLoc(map, out _, out double mv, out _, out Point ml);
                            evalCount++;
                            var locFull = new Point(ml.X * pyrFactor, ml.Y * pyrFactor);
                            candidates.Add((mv, a, s, locFull, new Size(cached.Width, cached.Height)));
                        }
                        else
                        {
                            // 매칭맵의 상위 N 로컬 피크 추출 (한 (각도, 스케일)당)
                            int picksPerMap = Math.Max(2, MaxInstances);
                            foreach (var (mv, ml) in PickLocalPeaks(map, picksPerMap, cached.Width, cached.Height))
                            {
                                evalCount++;
                                var locFull = new Point(ml.X * pyrFactor, ml.Y * pyrFactor);
                                candidates.Add((mv, a, s, locFull, new Size(cached.Width, cached.Height)));
                            }
                        }
                    }
                }

                if (candidates.Count == 0) return new List<MatchInstance>();

                // 점수 상위만 정밀화 (전체 후보가 너무 많으면 비효율)
                candidates.Sort((x, y) => y.score.CompareTo(x.score));
                int topN = Math.Min(candidatePoolSize, candidates.Count);

                // ── Fine pass (풀 해상도) ──
                double angleRange = coarseStep;
                double scaleRange = ScaleStep;
                int locMargin = pyrFactor * 2;
                var refined = new List<MatchInstance>(topN);

                for (int k = 0; k < topN; k++)
                {
                    var c = candidates[k];
                    double bestScore = -1, bestA = c.angle, bestS = c.scale;
                    Point bestLoc = new Point(-1, -1);
                    Size bestSize = new Size(0, 0);

                    for (double da = -angleRange; da <= angleRange + 1e-6; da += AngleStep)
                    {
                        double fa = c.angle + da;
                        for (double ds = -scaleRange; ds <= scaleRange + 1e-6; ds += ScaleStep)
                        {
                            double fs = c.scale + ds;
                            if (fs < MinScale - 1e-6 || fs > MaxScale + 1e-6) continue;

                            var cached = TryGetCached(fa, fs, 0);
                            if (cached == null) continue;
                            int nw = cached.Width, nh = cached.Height;
                            if (nw < 4 || nh < 4) continue;
                            if (nw > gray.Width || nh > gray.Height) continue;

                            int sx0 = Math.Max(0, c.locFull.X - locMargin);
                            int sy0 = Math.Max(0, c.locFull.Y - locMargin);
                            int sx1 = Math.Min(gray.Width, c.locFull.X + nw + locMargin);
                            int sy1 = Math.Min(gray.Height, c.locFull.Y + nh + locMargin);
                            int sw = sx1 - sx0, sh = sy1 - sy0;
                            if (sw < nw || sh < nh) continue;

                            using var graySub = new Mat(gray, new Rect(sx0, sy0, sw, sh));
                            using var map = new Mat();
                            Cv2.MatchTemplate(graySub, cached, map, TemplateMatchModes.CCoeffNormed);
                            Cv2.MinMaxLoc(map, out _, out double mv, out _, out Point ml);
                            evalCount++;

                            if (mv > bestScore)
                            {
                                bestScore = mv;
                                bestA = fa; bestS = fs;
                                bestLoc = new Point(ml.X + sx0, ml.Y + sy0);
                                bestSize = new Size(nw, nh);
                            }
                        }
                    }

                    if (bestLoc.X >= 0)
                        refined.Add(new MatchInstance(bestScore, bestA, bestS, bestLoc, bestSize));
                }

                // ── NMS — 점수 정렬 후 중심 거리 기반 비최대 억제 ──
                return ApplyNms(refined, MaxInstances, NmsDistanceFactor, ScoreThreshold);
            }
            finally
            {
                foreach (var m in grayLevels) m.Dispose();
            }
        }

        /// <summary>
        /// 매칭맵에서 상위 N개의 로컬 피크를 추출. 추출 후 그 위치 주변(템플릿 절반 크기)을 마스킹하여 중복 피크 방지.
        /// </summary>
        private static IEnumerable<(double Score, Point Loc)> PickLocalPeaks(Mat map, int n, int tplW, int tplH)
        {
            using var work = map.Clone();
            int rx = Math.Max(2, tplW / 2);
            int ry = Math.Max(2, tplH / 2);
            for (int i = 0; i < n; i++)
            {
                Cv2.MinMaxLoc(work, out _, out double mv, out _, out Point ml);
                if (mv < 0.1 || ml.X < 0) yield break;
                yield return (mv, ml);
                // 주변 마스킹 (다음 반복에서 같은 피크 안 잡히도록)
                int x0 = Math.Max(0, ml.X - rx);
                int y0 = Math.Max(0, ml.Y - ry);
                int x1 = Math.Min(work.Width, ml.X + rx);
                int y1 = Math.Min(work.Height, ml.Y + ry);
                if (x1 > x0 && y1 > y0)
                {
                    using var roi = new Mat(work, new Rect(x0, y0, x1 - x0, y1 - y0));
                    roi.SetTo(Scalar.All(-1.0));
                }
            }
        }

        /// <summary>
        /// 점수 내림차순 정렬 → 임계값 컷오프 → 중심 거리 기반 NMS → 상위 maxN 반환.
        /// </summary>
        private static List<MatchInstance> ApplyNms(List<MatchInstance> candidates, int maxN, double distFactor, double scoreThreshold)
        {
            var filtered = candidates
                .Where(c => c.Score >= scoreThreshold)
                .OrderByDescending(c => c.Score)
                .ToList();

            var keep = new List<MatchInstance>();
            foreach (var c in filtered)
            {
                if (keep.Count >= maxN) break;
                double cx = c.Loc.X + c.Size.Width / 2.0;
                double cy = c.Loc.Y + c.Size.Height / 2.0;
                bool overlap = false;
                foreach (var k in keep)
                {
                    double kx = k.Loc.X + k.Size.Width / 2.0;
                    double ky = k.Loc.Y + k.Size.Height / 2.0;
                    double d = Math.Sqrt((cx - kx) * (cx - kx) + (cy - ky) * (cy - ky));
                    double minSide = Math.Min(c.Size.Width, c.Size.Height);
                    if (d < minSide * distFactor) { overlap = true; break; }
                }
                if (!overlap) keep.Add(c);
            }
            return keep;
        }

        /// <summary>
        /// 가우시안 피라미드 빌드. 너무 작아지면 (16px 미만) 조기 중단.
        /// 반환 배열은 호출자가 Dispose 책임.
        /// </summary>
        private static List<Mat> BuildPyramid(Mat src, int numLevels)
        {
            var levels = new List<Mat> { src.Clone() };
            for (int i = 1; i < numLevels; i++)
            {
                var prev = levels[levels.Count - 1];
                if (prev.Width < 32 || prev.Height < 32) break;
                var down = new Mat();
                Cv2.PyrDown(prev, down);
                levels.Add(down);
            }
            return levels;
        }

        /// <summary>
        /// 템플릿을 angle도 회전. 회전된 bounding box 크기로 새 Mat 반환 (검은 패딩).
        /// </summary>
        private static Mat RotateAround(Mat src, double angle)
        {
            if (Math.Abs(angle) < 1e-6) return src.Clone();

            double rad = angle * Math.PI / 180.0;
            double cos = Math.Abs(Math.Cos(rad));
            double sin = Math.Abs(Math.Sin(rad));
            int newW = (int)Math.Ceiling(src.Width * cos + src.Height * sin);
            int newH = (int)Math.Ceiling(src.Width * sin + src.Height * cos);

            var center = new Point2f(src.Width / 2f, src.Height / 2f);
            using var M = Cv2.GetRotationMatrix2D(center, angle, 1.0);
            // 평행이동 보정 — 회전된 박스가 새 캔버스에 중심 정렬되도록
            M.Set(0, 2, M.Get<double>(0, 2) + (newW - src.Width) / 2.0);
            M.Set(1, 2, M.Get<double>(1, 2) + (newH - src.Height) / 2.0);

            var dst = new Mat();
            Cv2.WarpAffine(src, dst, M, new Size(newW, newH),
                InterpolationFlags.Cubic, BorderTypes.Constant, Scalar.All(0));
            return dst;
        }

        /// <summary>
        /// Rect를 이미지 경계 안으로 정규화 (음수 W/H, out-of-bounds 처리).
        /// </summary>
        private static Rect ClipRect(Rect r, int imgW, int imgH)
        {
            int x1 = Math.Min(r.X, r.X + r.Width);
            int y1 = Math.Min(r.Y, r.Y + r.Height);
            int x2 = Math.Max(r.X, r.X + r.Width);
            int y2 = Math.Max(r.Y, r.Y + r.Height);
            int sx = Math.Clamp(x1, 0, imgW);
            int sy = Math.Clamp(y1, 0, imgH);
            int ex = Math.Clamp(x2, 0, imgW);
            int ey = Math.Clamp(y2, 0, imgH);
            return new Rect(sx, sy, ex - sx, ey - sy);
        }

        private static void DrawRotatedBox(Mat img, double cx, double cy, double w, double h, double angle, Scalar color)
        {
            var rect = new RotatedRect(new Point2f((float)cx, (float)cy),
                                       new Size2f((float)w, (float)h),
                                       (float)angle);
            var pts = rect.Points();
            for (int i = 0; i < 4; i++)
                Cv2.Line(img,
                    new Point((int)pts[i].X, (int)pts[i].Y),
                    new Point((int)pts[(i + 1) % 4].X, (int)pts[(i + 1) % 4].Y),
                    color, 2);
        }

        public override List<string> GetAvailableResultKeys()
        {
            var keys = new List<string>
            {
                "Success", "Score", "CenterX", "CenterY", "Angle", "Scale",
                "MatchWidth", "MatchHeight", "MatchCount", "GridEvaluations"
            };
            // Match{i}_* 키 (실제 인스턴스 수만큼 + 여분)
            int slots = Math.Max(MaxInstances, 4);
            for (int i = 0; i < slots; i++)
            {
                keys.Add($"Match{i}_Score");
                keys.Add($"Match{i}_CenterX");
                keys.Add($"Match{i}_CenterY");
                keys.Add($"Match{i}_Angle");
                keys.Add($"Match{i}_Scale");
                keys.Add($"Match{i}_Width");
                keys.Add($"Match{i}_Height");
            }
            return keys;
        }

        public override VisionToolBase Clone()
        {
            var clone = new ShapeMatchTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                ROIAngle = this.ROIAngle,
                ROICenterX = this.ROICenterX,
                ROICenterY = this.ROICenterY,
                AngleStep = this.AngleStep,
                MinScale = this.MinScale,
                MaxScale = this.MaxScale,
                ScaleStep = this.ScaleStep,
                ScoreThreshold = this.ScoreThreshold,
                NumPyramidLevels = this.NumPyramidLevels,
                TopCandidates = this.TopCandidates,
                MaxInstances = this.MaxInstances,
                NmsDistanceFactor = this.NmsDistanceFactor,
                SearchRegion = this.SearchRegion,
                UseSearchRegion = this.UseSearchRegion,
                TemplatePngBytes = this.TemplatePngBytes == null ? null : (byte[])this.TemplatePngBytes.Clone()
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
