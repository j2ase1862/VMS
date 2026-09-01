// NativeVision.cpp — AVX2+FMA accelerated scoring for FeatureMatchTool Phase 2
// + OpenMP Hough Voting for Phase 1
// + Fused Sobel+Magnitude preprocessing
// Build: cl /O2 /arch:AVX2 /fp:fast /LD /EHsc /MD /openmp NativeVision.cpp /Fe:NativeVision.dll

#include <immintrin.h>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <omp.h>
#include <algorithm>

#define EXPORT extern "C" __declspec(dllexport)

// ─── Fused Sobel X, Y + Magnitude in one pass (AVX2) ────────────────────────
// Replaces 3 separate OpenCV calls with a single memory traversal.
// Input: 8-bit grayscale; Output: float Sobel X, Sobel Y, Magnitude
// Uses Sobel 3×3 kernel with AVX2 for horizontal rows.

EXPORT void __cdecl ComputeGradientNative(
    const uint8_t* __restrict gray,
    int width, int height, int stride,
    float* __restrict outDx,
    float* __restrict outDy,
    float* __restrict outMag)
{
    // Sobel 3×3 kernels:
    // Kx = [-1 0 1; -2 0 2; -1 0 1]
    // Ky = [-1 -2 -1;  0  0  0;  1  2  1]
    // Border pixels: zero gradient

    // Zero border rows
    memset(outDx, 0, width * sizeof(float));
    memset(outDy, 0, width * sizeof(float));
    memset(outMag, 0, width * sizeof(float));
    memset(outDx + (height - 1) * width, 0, width * sizeof(float));
    memset(outDy + (height - 1) * width, 0, width * sizeof(float));
    memset(outMag + (height - 1) * width, 0, width * sizeof(float));

    #pragma omp parallel for schedule(static)
    for (int y = 1; y < height - 1; y++)
    {
        const uint8_t* r0 = gray + (y - 1) * stride;
        const uint8_t* r1 = gray + y * stride;
        const uint8_t* r2 = gray + (y + 1) * stride;
        float* dx = outDx + y * width;
        float* dy = outDy + y * width;
        float* mg = outMag + y * width;

        // Zero border columns
        dx[0] = dy[0] = mg[0] = 0.0f;
        dx[width - 1] = dy[width - 1] = mg[width - 1] = 0.0f;

        int x = 1;
        // AVX2 path: process 8 pixels at a time
        for (; x + 8 < width - 1; x += 8)
        {
            // Load 10 bytes per row (need x-1 .. x+8) and convert to int32
            // Simpler: load 3 columns per row as int16, compute
            // Use scalar-promoted approach: load individual pixels
            __m256 r0_m1 = _mm256_cvtepi32_ps(_mm256_cvtepu8_epi32(
                _mm_loadl_epi64((__m128i*)(r0 + x - 1))));
            __m256 r0_0  = _mm256_cvtepi32_ps(_mm256_cvtepu8_epi32(
                _mm_loadl_epi64((__m128i*)(r0 + x))));
            __m256 r0_p1 = _mm256_cvtepi32_ps(_mm256_cvtepu8_epi32(
                _mm_loadl_epi64((__m128i*)(r0 + x + 1))));
            __m256 r1_m1 = _mm256_cvtepi32_ps(_mm256_cvtepu8_epi32(
                _mm_loadl_epi64((__m128i*)(r1 + x - 1))));
            __m256 r1_p1 = _mm256_cvtepi32_ps(_mm256_cvtepu8_epi32(
                _mm_loadl_epi64((__m128i*)(r1 + x + 1))));
            __m256 r2_m1 = _mm256_cvtepi32_ps(_mm256_cvtepu8_epi32(
                _mm_loadl_epi64((__m128i*)(r2 + x - 1))));
            __m256 r2_0  = _mm256_cvtepi32_ps(_mm256_cvtepu8_epi32(
                _mm_loadl_epi64((__m128i*)(r2 + x))));
            __m256 r2_p1 = _mm256_cvtepi32_ps(_mm256_cvtepu8_epi32(
                _mm_loadl_epi64((__m128i*)(r2 + x + 1))));

            __m256 two = _mm256_set1_ps(2.0f);

            // Gx = -r0_m1 + r0_p1 - 2*r1_m1 + 2*r1_p1 - r2_m1 + r2_p1
            __m256 gx = _mm256_sub_ps(r0_p1, r0_m1);
            gx = _mm256_fmadd_ps(two, _mm256_sub_ps(r1_p1, r1_m1), gx);
            gx = _mm256_add_ps(gx, _mm256_sub_ps(r2_p1, r2_m1));

            // Gy = -r0_m1 - 2*r0_0 - r0_p1 + r2_m1 + 2*r2_0 + r2_p1
            __m256 gy = _mm256_sub_ps(r2_m1, r0_m1);
            gy = _mm256_fmadd_ps(two, _mm256_sub_ps(r2_0, r0_0), gy);
            gy = _mm256_add_ps(gy, _mm256_sub_ps(r2_p1, r0_p1));

            // Magnitude = sqrt(gx² + gy²)
            __m256 mag = _mm256_sqrt_ps(
                _mm256_fmadd_ps(gx, gx, _mm256_mul_ps(gy, gy)));

            _mm256_storeu_ps(dx + x, gx);
            _mm256_storeu_ps(dy + x, gy);
            _mm256_storeu_ps(mg + x, mag);
        }

        // Scalar remainder
        for (; x < width - 1; x++)
        {
            float gx = -(float)r0[x-1] + (float)r0[x+1]
                      - 2.0f*(float)r1[x-1] + 2.0f*(float)r1[x+1]
                      - (float)r2[x-1] + (float)r2[x+1];
            float gy = -(float)r0[x-1] - 2.0f*(float)r0[x] - (float)r0[x+1]
                      + (float)r2[x-1] + 2.0f*(float)r2[x] + (float)r2[x+1];
            dx[x] = gx;
            dy[x] = gy;
            mg[x] = sqrtf(gx*gx + gy*gy);
        }
    }
}

// ─── Per-pixel scoring (SIMD gather version) ─────────────────────────────────
// offsets[i] = ry[i] * imgW + rx[i]  (pre-computed by caller)

static double EvaluateNativeInternal(
    int px, int py,
    const int* __restrict offsets,
    const float* __restrict rdx, const float* __restrict rdy,
    const float* __restrict dxImg, const float* __restrict dyImg, const float* __restrict magImg,
    int imgW, int N,
    float thresh, float greedy,
    int contrastInvariant)
{
    float sum = 0.0f;
    int earlyN = N / 5;
    float earlyThresh = thresh * (1.0f - greedy);

    __m256 vsum = _mm256_setzero_ps();
    __m256 veps = _mm256_set1_ps(0.001f);
    __m256 absMask = _mm256_castsi256_ps(_mm256_set1_epi32(0x7FFFFFFF));

    int base = py * imgW + px;
    __m256i vbase = _mm256_set1_epi32(base);

    int vecN = N & ~7;
    for (int i = 0; i < vecN; i += 8)
    {
        // SIMD gather: compute indices and gather from all three images
        __m256i voff = _mm256_loadu_si256((const __m256i*)(offsets + i));
        __m256i vidx = _mm256_add_epi32(voff, vbase);

        __m256 vdx  = _mm256_i32gather_ps(dxImg,  vidx, 4);
        __m256 vdy  = _mm256_i32gather_ps(dyImg,  vidx, 4);
        __m256 vmag = _mm256_i32gather_ps(magImg, vidx, 4);

        __m256 vrdx = _mm256_loadu_ps(rdx + i);
        __m256 vrdy = _mm256_loadu_ps(rdy + i);

        // dot = rdx*dx + rdy*dy  (FMA)
        __m256 dot = _mm256_fmadd_ps(vrdx, vdx, _mm256_mul_ps(vrdy, vdy));

        // mask: true where mag > eps
        __m256 mask = _mm256_cmp_ps(vmag, veps, _CMP_GT_OS);

        // Reciprocal approximation (~12-bit precision)
        __m256 invMag = _mm256_and_ps(_mm256_rcp_ps(vmag), mask);
        __m256 val = _mm256_mul_ps(dot, invMag);

        if (contrastInvariant)
            val = _mm256_and_ps(val, absMask);

        vsum = _mm256_add_ps(vsum, val);

        // Early exit after earlyN points
        if (i + 8 >= earlyN && i + 8 < vecN)
        {
            __m128 lo = _mm256_castps256_ps128(vsum);
            __m128 hi = _mm256_extractf128_ps(vsum, 1);
            __m128 s = _mm_add_ps(lo, hi);
            s = _mm_add_ps(s, _mm_movehl_ps(s, s));
            s = _mm_add_ss(s, _mm_shuffle_ps(s, s, 1));
            float partial = _mm_cvtss_f32(s);
            if (partial / (i + 8) < earlyThresh) return 0.0;
        }
    }

    // Horizontal sum
    {
        __m128 lo = _mm256_castps256_ps128(vsum);
        __m128 hi = _mm256_extractf128_ps(vsum, 1);
        __m128 s = _mm_add_ps(lo, hi);
        s = _mm_add_ps(s, _mm_movehl_ps(s, s));
        s = _mm_add_ss(s, _mm_shuffle_ps(s, s, 1));
        sum = _mm_cvtss_f32(s);
    }

    // Scalar remainder using offsets
    for (int i = vecN; i < N; i++)
    {
        int idx = base + offsets[i];
        float m = magImg[idx];
        if (m > 0.001f)
        {
            float contrib = (rdx[i] * dxImg[idx] + rdy[i] * dyImg[idx]) / m;
            sum += contrastInvariant ? fabsf(contrib) : contrib;
        }
    }

    return (double)sum / N;
}

// ─── Legacy per-pixel scoring (external API, signature unchanged) ────────────

EXPORT double __cdecl EvaluateNative(
    int px, int py,
    const int* __restrict rx, const int* __restrict ry,
    const float* __restrict rdx, const float* __restrict rdy,
    const float* __restrict dxImg, const float* __restrict dyImg, const float* __restrict magImg,
    int imgW, int N,
    float thresh, float greedy,
    int contrastInvariant)
{
    // Build offsets on the fly for legacy callers
    int alignedN = (N + 7) & ~7;
    int* offsets = (int*)_aligned_malloc(alignedN * sizeof(int), 32);
    if (!offsets) return 0.0;
    for (int i = 0; i < N; i++)
        offsets[i] = ry[i] * imgW + rx[i];
    // Zero-pad remainder for safe SIMD load
    for (int i = N; i < alignedN; i++)
        offsets[i] = 0;

    double result = EvaluateNativeInternal(
        px, py, offsets, rdx, rdy, dxImg, dyImg, magImg,
        imgW, N, thresh, greedy, contrastInvariant);

    _aligned_free(offsets);
    return result;
}

// ─── Batch: score entire refinement grid for one pose ────────────────────────

EXPORT double __cdecl EvaluateBatchNative(
    int baseCx, int baseCy, int refRadius,
    const int* __restrict rx, const int* __restrict ry,
    const float* __restrict rdx, const float* __restrict rdy,
    const float* __restrict dxImg, const float* __restrict dyImg, const float* __restrict magImg,
    int imgW, int imgH, int N, int margin,
    float thresh, float greedy,
    int* outDx, int* outDy,
    int contrastInvariant)
{
    // Pre-compute offsets once for all grid positions
    int alignedN = (N + 7) & ~7;
    int* offsets = (int*)_aligned_malloc(alignedN * sizeof(int), 32);
    if (!offsets) { *outDx = 0; *outDy = 0; return 0.0; }
    for (int i = 0; i < N; i++)
        offsets[i] = ry[i] * imgW + rx[i];
    for (int i = N; i < alignedN; i++)
        offsets[i] = 0;

    double bestScore = 0.0;
    int bestDx = 0, bestDy = 0;

    for (int dy = -refRadius; dy <= refRadius; dy++)
    {
        int py = baseCy + dy;
        if (py < margin || py >= imgH - margin) continue;

        for (int dx = -refRadius; dx <= refRadius; dx++)
        {
            int px = baseCx + dx;
            if (px < margin || px >= imgW - margin) continue;

            double score = EvaluateNativeInternal(
                px, py, offsets, rdx, rdy,
                dxImg, dyImg, magImg,
                imgW, N, thresh, greedy,
                contrastInvariant);

            if (score > bestScore)
            {
                bestScore = score;
                bestDx = dx;
                bestDy = dy;
            }
        }
    }

    _aligned_free(offsets);
    *outDx = bestDx;
    *outDy = bestDy;
    return bestScore;
}

// ─── Native Hough Voting with OpenMP (Phase 1) ──────────────────────────────

// Candidate for Hough voting passes (file scope so multiple exports share it)
struct HoughCandidate {
    double angle, cx, cy;
    int votes;
};

// ── Multi-peak accumulator extraction ───────────────────────────────────────
// Up to maxPeaks spatially separated peaks per accumulator: repeated max-scan,
// zeroing a (2*sepBins+1)² neighborhood after each pick so the same instance
// is not reported twice. Destroys acc (caller clears it per angle anyway).
// Needed for multi-instance: one accumulator holds one peak per object, but
// the single-max scan only ever surfaced the strongest instance per angle.
#define NV_MAX_PEAKS_PER_ANGLE 32

static int ExtractAccPeaks(int* acc, int bW, int bH, int maxPeaks, int sepBins,
                           int* outVotes, int* outIdx)
{
    int accLen = bW * bH;
    int found = 0;
    while (found < maxPeaks)
    {
        int maxVote = 0, maxIdx = 0;
        for (int i = 0; i < accLen; i++)
            if (acc[i] > maxVote) { maxVote = acc[i]; maxIdx = i; }
        if (maxVote <= 0) break;
        outVotes[found] = maxVote;
        outIdx[found] = maxIdx;
        found++;
        if (found >= maxPeaks) break;

        int px = maxIdx % bW, py = maxIdx / bW;
        int x0 = px - sepBins; if (x0 < 0) x0 = 0;
        int y0 = py - sepBins; if (y0 < 0) y0 = 0;
        int x1 = px + sepBins; if (x1 >= bW) x1 = bW - 1;
        int y1 = py + sepBins; if (y1 >= bH) y1 = bH - 1;
        for (int yy = y0; yy <= y1; yy++)
            memset(acc + yy * bW + x0, 0, (size_t)(x1 - x0 + 1) * sizeof(int));
    }
    return found;
}

// ── Shared voting core: coarse sweep → thread-local topK merge → fine sweep ──
// Fills *outFine (fine results, count = return value) and *outCoarse (topK,
// vote-desc). Caller must _aligned_free both.
// peaksPerAngle: how many spatially separated peaks to keep per accumulator
// (1 = legacy single-peak behavior). sepBins (accumulator bins) separates them.
static int HoughVoteCollect(
    const float* modelX, const float* modelY, int modelCount,
    const int* binOffsets, const int* binIndices,
    int numGradBins,
    const int* searchX, const int* searchY, const int* searchBin, int searchEdgeCount,
    int voteWidth, int voteHeight,
    double angleStart, double angleExtent,
    double coarseAngleStep, double fineAngleStep,
    int topK,
    double invScale,
    int binShiftBits,
    int peaksPerAngle, int sepBins,
    HoughCandidate** outFine, HoughCandidate** outCoarse)
{
    if (peaksPerAngle < 1) peaksPerAngle = 1;
    if (peaksPerAngle > NV_MAX_PEAKS_PER_ANGLE) peaksPerAngle = NV_MAX_PEAKS_PER_ANGLE;
    if (sepBins < 1) sepBins = 1;
    const double PI = 3.14159265358979323846;
    const double DEG2RAD = PI / 180.0;
    double binWidthDeg = 360.0 / numGradBins;

    int bW = (voteWidth >> binShiftBits) + 1;
    int bH = (voteHeight >> binShiftBits) + 1;
    int accLen = bW * bH;

    // ── Pass 1: Coarse angle sweep ──
    int numCoarseAngles = (int)(angleExtent / coarseAngleStep) + 1;
    if (numCoarseAngles < 1) numCoarseAngles = 1;

    typedef HoughCandidate Candidate;

    Candidate* candidates = (Candidate*)_aligned_malloc(topK * sizeof(Candidate), 64);
    for (int i = 0; i < topK; i++) {
        candidates[i].angle = 0;
        candidates[i].cx = 0;
        candidates[i].cy = 0;
        candidates[i].votes = 0;
    }

    // Thread-local results for coarse pass
    int maxThreads = omp_get_max_threads();
    Candidate* threadBest = (Candidate*)_aligned_malloc(maxThreads * topK * sizeof(Candidate), 64);
    for (int t = 0; t < maxThreads * topK; t++) {
        threadBest[t].angle = 0;
        threadBest[t].cx = 0;
        threadBest[t].cy = 0;
        threadBest[t].votes = 0;
    }

    #pragma omp parallel
    {
        int tid = omp_get_thread_num();
        Candidate* myBest = threadBest + tid * topK;

        // Each thread gets its own accumulator
        int* acc = (int*)_aligned_malloc(accLen * sizeof(int), 64);
        int* rotXBuf = (int*)_aligned_malloc(modelCount * sizeof(int), 32);
        int* rotYBuf = (int*)_aligned_malloc(modelCount * sizeof(int), 32);

        #pragma omp for schedule(dynamic)
        for (int ai = 0; ai < numCoarseAngles; ai++)
        {
            double angle = angleStart + ai * coarseAngleStep;
            double rad = angle * DEG2RAD;
            double cosA = cos(rad);
            double sinA = sin(rad);

            // Rotate model points
            for (int i = 0; i < modelCount; i++)
            {
                rotXBuf[i] = (int)(( modelX[i] * cosA - modelY[i] * sinA) * invScale + 0.5);
                rotYBuf[i] = (int)(( modelX[i] * sinA + modelY[i] * cosA) * invScale + 0.5);
            }

            memset(acc, 0, accLen * sizeof(int));
            int binShift = (int)(angle / binWidthDeg + (angle >= 0 ? 0.5 : -0.5));

            for (int si = 0; si < searchEdgeCount; si++)
            {
                int ex = searchX[si], ey = searchY[si], sb = searchBin[si];
                for (int db = -1; db <= 1; db++)
                {
                    int modelBin = ((sb - binShift + db) % numGradBins + numGradBins) % numGradBins;
                    int bStart = binOffsets[modelBin];
                    int bEnd   = binOffsets[modelBin + 1];
                    for (int bi = bStart; bi < bEnd; bi++)
                    {
                        int j = binIndices[bi];
                        int cx = (ex - rotXBuf[j]) >> binShiftBits;
                        int cy = (ey - rotYBuf[j]) >> binShiftBits;
                        if ((unsigned)cx < (unsigned)bW && (unsigned)cy < (unsigned)bH)
                            acc[cy * bW + cx]++;
                    }
                }
            }

            // Extract peak(s) in accumulator — multi-instance keeps several
            // spatially separated peaks per angle instead of just the max.
            int pkVotes[NV_MAX_PEAKS_PER_ANGLE];
            int pkIdx[NV_MAX_PEAKS_PER_ANGLE];
            int nPk = ExtractAccPeaks(acc, bW, bH, peaksPerAngle, sepBins, pkVotes, pkIdx);

            for (int pk = 0; pk < nPk; pk++)
            {
                double peakCx = (pkIdx[pk] % bW) * (1 << binShiftBits) + (1 << binShiftBits) / 2;
                double peakCy = (pkIdx[pk] / bW) * (1 << binShiftBits) + (1 << binShiftBits) / 2;

                // Insertion sort into thread-local top K
                if (pkVotes[pk] > myBest[topK - 1].votes)
                {
                    myBest[topK - 1].angle = angle;
                    myBest[topK - 1].cx = peakCx;
                    myBest[topK - 1].cy = peakCy;
                    myBest[topK - 1].votes = pkVotes[pk];
                    // Bubble up
                    for (int k = topK - 1; k > 0 && myBest[k].votes > myBest[k-1].votes; k--)
                    {
                        Candidate tmp = myBest[k];
                        myBest[k] = myBest[k-1];
                        myBest[k-1] = tmp;
                    }
                }
            }
        }

        _aligned_free(acc);
        _aligned_free(rotXBuf);
        _aligned_free(rotYBuf);
    }

    // Merge thread-local top K into global top K
    for (int t = 0; t < maxThreads; t++)
    {
        Candidate* tb = threadBest + t * topK;
        for (int i = 0; i < topK; i++)
        {
            if (tb[i].votes > candidates[topK - 1].votes)
            {
                candidates[topK - 1] = tb[i];
                for (int k = topK - 1; k > 0 && candidates[k].votes > candidates[k-1].votes; k--)
                {
                    Candidate tmp = candidates[k];
                    candidates[k] = candidates[k-1];
                    candidates[k-1] = tmp;
                }
            }
        }
    }
    _aligned_free(threadBest);

    // Count actual valid candidates
    int validK = 0;
    for (int i = 0; i < topK; i++)
        if (candidates[i].votes > 0) validK++;
    if (validK == 0) validK = 1;

    // ── Pass 2: Fine refinement around each candidate ──
    int numFinePerCand = (int)(2.0 * coarseAngleStep / fineAngleStep) + 1;
    int fineResultCount = validK * numFinePerCand * peaksPerAngle;

    Candidate* fineResults = (Candidate*)_aligned_malloc(fineResultCount * sizeof(Candidate), 64);
    for (int i = 0; i < fineResultCount; i++) {
        fineResults[i].votes = 0;
    }

    int fineOffset = 0;
    for (int ci = 0; ci < validK; ci++)
    {
        double centerAngle = candidates[ci].angle;

        // peaksPerAngle > 1이면 같은 각도의 코스 후보가 위치만 달리해 여러 개
        // 들어온다 — 파인 스윕은 위치 무관 전체 누산기라 각도가 같으면 결과도
        // 동일하므로 중복 스윕을 건너뛴다 (해당 슬롯은 votes=0 유지).
        bool dupAngle = false;
        for (int cj = 0; cj < ci; cj++)
            if (candidates[cj].votes > 0 && candidates[cj].angle == centerAngle) { dupAngle = true; break; }
        if (dupAngle) { fineOffset += numFinePerCand * peaksPerAngle; continue; }

        double fineStart = centerAngle - coarseAngleStep;
        double fineEnd   = centerAngle + coarseAngleStep;
        int numFine = numFinePerCand;

        #pragma omp parallel
        {
            int* acc = (int*)_aligned_malloc(accLen * sizeof(int), 64);
            int* rotXBuf = (int*)_aligned_malloc(modelCount * sizeof(int), 32);
            int* rotYBuf = (int*)_aligned_malloc(modelCount * sizeof(int), 32);

            #pragma omp for schedule(dynamic)
            for (int fi = 0; fi < numFine; fi++)
            {
                double angle = fineStart + fi * fineAngleStep;
                if (angle < angleStart || angle > angleStart + angleExtent) {
                    fineResults[fineOffset + fi * peaksPerAngle].votes = 0;
                    continue;
                }

                double rad = angle * DEG2RAD;
                double cosA = cos(rad);
                double sinA = sin(rad);

                for (int i = 0; i < modelCount; i++)
                {
                    rotXBuf[i] = (int)((modelX[i] * cosA - modelY[i] * sinA) * invScale + 0.5);
                    rotYBuf[i] = (int)((modelX[i] * sinA + modelY[i] * cosA) * invScale + 0.5);
                }

                memset(acc, 0, accLen * sizeof(int));
                int binShift = (int)(angle / binWidthDeg + (angle >= 0 ? 0.5 : -0.5));

                for (int si = 0; si < searchEdgeCount; si++)
                {
                    int ex = searchX[si], ey = searchY[si], sb = searchBin[si];
                    for (int db = -1; db <= 1; db++)
                    {
                        int modelBin = ((sb - binShift + db) % numGradBins + numGradBins) % numGradBins;
                        int bStart = binOffsets[modelBin];
                        int bEnd   = binOffsets[modelBin + 1];
                        for (int bi = bStart; bi < bEnd; bi++)
                        {
                            int j = binIndices[bi];
                            int cx = (ex - rotXBuf[j]) >> binShiftBits;
                            int cy = (ey - rotYBuf[j]) >> binShiftBits;
                            if ((unsigned)cx < (unsigned)bW && (unsigned)cy < (unsigned)bH)
                                acc[cy * bW + cx]++;
                        }
                    }
                }

                int pkVotes[NV_MAX_PEAKS_PER_ANGLE];
                int pkIdx[NV_MAX_PEAKS_PER_ANGLE];
                int nPk = ExtractAccPeaks(acc, bW, bH, peaksPerAngle, sepBins, pkVotes, pkIdx);

                for (int pk = 0; pk < nPk; pk++)
                {
                    Candidate& fr = fineResults[fineOffset + fi * peaksPerAngle + pk];
                    fr.angle = angle;
                    fr.cx = (pkIdx[pk] % bW) * (1 << binShiftBits) + (1 << binShiftBits) / 2;
                    fr.cy = (pkIdx[pk] / bW) * (1 << binShiftBits) + (1 << binShiftBits) / 2;
                    fr.votes = pkVotes[pk];
                }
            }

            _aligned_free(acc);
            _aligned_free(rotXBuf);
            _aligned_free(rotYBuf);
        }
        fineOffset += numFine * peaksPerAngle;
    }

    *outFine = fineResults;
    *outCoarse = candidates;
    return fineResultCount;
}

EXPORT void __cdecl HoughVotingNative(
    const float* modelX, const float* modelY, int modelCount,
    const int* binOffsets, const int* binIndices,
    int numGradBins,
    const int* searchX, const int* searchY, const int* searchBin, int searchEdgeCount,
    int voteWidth, int voteHeight,
    double angleStart, double angleExtent,
    double coarseAngleStep, double fineAngleStep,
    int topK,
    double invScale,
    int binShiftBits,
    double* outBestCx, double* outBestCy, double* outBestAngle, int* outBestVotes)
{
    HoughCandidate* fineResults = nullptr;
    HoughCandidate* candidates = nullptr;
    int fineResultCount = HoughVoteCollect(
        modelX, modelY, modelCount, binOffsets, binIndices, numGradBins,
        searchX, searchY, searchBin, searchEdgeCount,
        voteWidth, voteHeight, angleStart, angleExtent,
        coarseAngleStep, fineAngleStep, topK, invScale, binShiftBits,
        1, 1,
        &fineResults, &candidates);

    // Find overall best from fine results
    int bestIdx = 0;
    for (int i = 1; i < fineResultCount; i++)
    {
        if (fineResults[i].votes > fineResults[bestIdx].votes)
            bestIdx = i;
    }

    if (fineResultCount > 0 && fineResults[bestIdx].votes > 0)
    {
        *outBestCx = fineResults[bestIdx].cx;
        *outBestCy = fineResults[bestIdx].cy;
        *outBestAngle = fineResults[bestIdx].angle;
        *outBestVotes = fineResults[bestIdx].votes;
    }
    else
    {
        // Fall back to coarse best
        *outBestCx = candidates[0].cx;
        *outBestCy = candidates[0].cy;
        *outBestAngle = candidates[0].angle;
        *outBestVotes = candidates[0].votes;
    }

    _aligned_free(fineResults);
    _aligned_free(candidates);
}

// ── Greedy NMS over fine results: votes-desc, minSeparation apart ──────────
// Falls back to the coarse best when the fine pass produced nothing (same as
// the single-peak path). Returns the number of peaks written to out arrays.
static int SelectSeparatedPeaks(
    const HoughCandidate* fineResults, int n, const HoughCandidate* coarse,
    double minSeparation, int outK,
    double* outCx, double* outCy, double* outAngle, int* outVotes)
{
    bool* used = (bool*)_aligned_malloc((n > 0 ? n : 1) * sizeof(bool), 16);
    memset(used, 0, (n > 0 ? n : 1) * sizeof(bool));
    double minSepSq = minSeparation * minSeparation;

    int filled = 0;
    while (filled < outK)
    {
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (used[i] || fineResults[i].votes <= 0) continue;
            if (best < 0 || fineResults[i].votes > fineResults[best].votes) best = i;
        }
        if (best < 0) break;
        used[best] = true;

        // Nearby peak = same instance seen at another angle → keep best only
        bool dup = false;
        for (int k = 0; k < filled; k++)
        {
            double dx = fineResults[best].cx - outCx[k];
            double dy = fineResults[best].cy - outCy[k];
            if (dx * dx + dy * dy < minSepSq) { dup = true; break; }
        }
        if (dup) continue;

        outCx[filled] = fineResults[best].cx;
        outCy[filled] = fineResults[best].cy;
        outAngle[filled] = fineResults[best].angle;
        outVotes[filled] = fineResults[best].votes;
        filled++;
    }

    if (filled == 0)   // fine pass empty → coarse best
    {
        outCx[0] = coarse[0].cx;
        outCy[0] = coarse[0].cy;
        outAngle[0] = coarse[0].angle;
        outVotes[0] = coarse[0].votes;
        filled = 1;
    }

    _aligned_free(used);
    return filled;
}

// ── Top-K spatially distinct vote peaks (NMS) ──────────────────────────────
// The single best vote peak can belong to a distractor (background structure,
// neighboring part, shadow cluster); the true instance then never reaches the
// gradient-scoring stage. Returning K spatially separated peaks lets the C#
// refinement score each one and pick the true match. minSeparation is in
// vote-image (coarse pyramid) pixels. Returns the number of peaks filled.
EXPORT int __cdecl HoughVotingTopKNative(
    const float* modelX, const float* modelY, int modelCount,
    const int* binOffsets, const int* binIndices,
    int numGradBins,
    const int* searchX, const int* searchY, const int* searchBin, int searchEdgeCount,
    int voteWidth, int voteHeight,
    double angleStart, double angleExtent,
    double coarseAngleStep, double fineAngleStep,
    int topK,
    double invScale,
    int binShiftBits,
    double minSeparation, int outK,
    double* outCx, double* outCy, double* outAngle, int* outVotes)
{
    HoughCandidate* fineResults = nullptr;
    HoughCandidate* candidates = nullptr;
    int n = HoughVoteCollect(
        modelX, modelY, modelCount, binOffsets, binIndices, numGradBins,
        searchX, searchY, searchBin, searchEdgeCount,
        voteWidth, voteHeight, angleStart, angleExtent,
        coarseAngleStep, fineAngleStep, topK, invScale, binShiftBits,
        1, 1,
        &fineResults, &candidates);

    int filled = SelectSeparatedPeaks(fineResults, n, candidates, minSeparation, outK,
                                      outCx, outCy, outAngle, outVotes);

    _aligned_free(fineResults);
    _aligned_free(candidates);
    return filled;
}

// ── Multi-instance voting: Top-K NMS + multiple peaks per angle accumulator ──
// HoughVotingTopKNative only surfaces the strongest peak of each angle's
// accumulator, so several identical objects at the same orientation collapse
// into a single candidate. peaksPerAngle keeps up to that many spatially
// separated peaks per angle (separation derived from minSeparation), letting
// every instance reach the C# refinement stage.
EXPORT int __cdecl HoughVotingTopKMultiNative(
    const float* modelX, const float* modelY, int modelCount,
    const int* binOffsets, const int* binIndices,
    int numGradBins,
    const int* searchX, const int* searchY, const int* searchBin, int searchEdgeCount,
    int voteWidth, int voteHeight,
    double angleStart, double angleExtent,
    double coarseAngleStep, double fineAngleStep,
    int topK,
    double invScale,
    int binShiftBits,
    int peaksPerAngle,
    double minSeparation, int outK,
    double* outCx, double* outCy, double* outAngle, int* outVotes)
{
    int sepBins = (int)(minSeparation / (double)(1 << binShiftBits));
    if (sepBins < 1) sepBins = 1;

    HoughCandidate* fineResults = nullptr;
    HoughCandidate* candidates = nullptr;
    int n = HoughVoteCollect(
        modelX, modelY, modelCount, binOffsets, binIndices, numGradBins,
        searchX, searchY, searchBin, searchEdgeCount,
        voteWidth, voteHeight, angleStart, angleExtent,
        coarseAngleStep, fineAngleStep, topK, invScale, binShiftBits,
        peaksPerAngle, sepBins,
        &fineResults, &candidates);

    int filled = SelectSeparatedPeaks(fineResults, n, candidates, minSeparation, outK,
                                      outCx, outCy, outAngle, outVotes);

    _aligned_free(fineResults);
    _aligned_free(candidates);
    return filled;
}

// ─── Batch: score ALL poses × entire refinement grid in one call ─────────────
// Eliminates per-pose C#→native P/Invoke overhead; OpenMP across poses.

EXPORT double __cdecl EvaluateAllPosesNative(
    int baseCx, int baseCy, int refRadius,
    const int* allRx, const int* allRy,
    const float* allRdx, const float* allRdy,
    const int* margins,
    int poseCount, int N,
    const float* dxImg, const float* dyImg, const float* magImg,
    int imgW, int imgH,
    float thresh, float greedy,
    int* outBestDx, int* outBestDy, int* outBestPoseIdx,
    int contrastInvariant)
{
    double globalBestScore = 0.0;
    int globalBestDx = 0, globalBestDy = 0, globalBestPose = 0;
    int alignedN = (N + 7) & ~7;

    #pragma omp parallel
    {
        double localBest = 0.0;
        int localDx = 0, localDy = 0, localPose = 0;

        int* offsets = (int*)_aligned_malloc(alignedN * sizeof(int), 32);

        #pragma omp for schedule(dynamic)
        for (int pi = 0; pi < poseCount; pi++)
        {
            const int* rx  = allRx  + (int64_t)pi * N;
            const int* ry  = allRy  + (int64_t)pi * N;
            const float* rdx = allRdx + (int64_t)pi * N;
            const float* rdy = allRdy + (int64_t)pi * N;
            int margin = margins[pi];

            for (int i = 0; i < N; i++)
                offsets[i] = ry[i] * imgW + rx[i];
            for (int i = N; i < alignedN; i++)
                offsets[i] = 0;

            for (int dy = -refRadius; dy <= refRadius; dy++)
            {
                int py = baseCy + dy;
                if (py < margin || py >= imgH - margin) continue;

                for (int dx = -refRadius; dx <= refRadius; dx++)
                {
                    int px = baseCx + dx;
                    if (px < margin || px >= imgW - margin) continue;

                    double score = EvaluateNativeInternal(
                        px, py, offsets, rdx, rdy,
                        dxImg, dyImg, magImg,
                        imgW, N, thresh, greedy,
                        contrastInvariant);

                    if (score > localBest)
                    {
                        localBest = score;
                        localDx = dx;
                        localDy = dy;
                        localPose = pi;
                    }
                }
            }
        }

        _aligned_free(offsets);

        #pragma omp critical
        {
            if (localBest > globalBestScore)
            {
                globalBestScore = localBest;
                globalBestDx = localDx;
                globalBestDy = localDy;
                globalBestPose = localPose;
            }
        }
    }

    *outBestDx = globalBestDx;
    *outBestDy = globalBestDy;
    *outBestPoseIdx = globalBestPose;
    return globalBestScore;
}
