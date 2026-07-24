using System.Buffers;
using System.Numerics;
using VMS.Camera.Models;

namespace VMS.Camera.Utils
{
    /// <summary>
    /// 4x4 동차 변환 행렬 기반 3D 좌표 변환 유틸리티
    /// Phase 3 로드맵: P_base = T_base_tcp × T_tcp_cam × P_cam
    /// </summary>
    public static class TransformUtils
    {
        /// <summary>
        /// 포인트 클라우드의 모든 점에 4x4 변환 행렬 적용 (병렬 처리)
        /// </summary>
        /// <param name="source">원본 포인트 클라우드 (카메라 좌표계)</param>
        /// <param name="transform">4x4 변환 행렬</param>
        /// <returns>변환된 새 포인트 클라우드 (월드 좌표계)</returns>
        public static PointCloudData TransformPointCloud(PointCloudData source, Matrix4x4 transform)
        {
            int count = source.PointCount;

            var result = new PointCloudData
            {
                Name = source.Name + "_transformed",
                Positions = new Vector3[count],
                Colors = new System.Windows.Media.Color[count],
                GridWidth = source.GridWidth,
                GridHeight = source.GridHeight,
            };
            result.PointCount = count;

            // 색상 복사
            Array.Copy(source.Colors, result.Colors, count);

            // 병렬 행렬 변환
            Parallel.For(0, count, i =>
            {
                result.Positions[i] = Vector3.Transform(source.Positions[i], transform);
            });

            return result;
        }

        /// <summary>
        /// 로봇 포즈와 핸드-아이 행렬을 합성하여 최종 변환 행렬 생성
        /// M = T_base_tcp × T_tcp_cam
        /// </summary>
        /// <param name="robotPose">촬영 시점 로봇 포즈 (T_base_tcp)</param>
        /// <param name="handEyeMatrix">핸드-아이 캘리브레이션 결과 (T_tcp_cam)</param>
        public static Matrix4x4 ComposeFinalTransform(RobotPose robotPose, Matrix4x4 handEyeMatrix)
        {
            var robotMatrix = robotPose.ToMatrix4x4();
            return handEyeMatrix * robotMatrix; // System.Numerics: row-major 순서
        }

        /// <summary>
        /// 다수의 포인트 클라우드를 하나로 병합
        /// </summary>
        public static PointCloudData MergePointClouds(IReadOnlyList<PointCloudData> clouds, string name = "Merged")
        {
            int totalCount = 0;
            foreach (var cloud in clouds)
                totalCount += cloud.PointCount;

            if (totalCount == 0)
            {
                return new PointCloudData
                {
                    Name = name,
                    Positions = Array.Empty<Vector3>(),
                    Colors = Array.Empty<System.Windows.Media.Color>()
                };
            }

            var positions = new Vector3[totalCount];
            var colors = new System.Windows.Media.Color[totalCount];
            int offset = 0;

            foreach (var cloud in clouds)
            {
                int count = cloud.PointCount;
                Array.Copy(cloud.Positions, 0, positions, offset, count);
                Array.Copy(cloud.Colors, 0, colors, offset, count);
                offset += count;
            }

            return new PointCloudData
            {
                Name = name,
                Positions = positions,
                Colors = colors,
                PointCount = totalCount
            };
        }

        /// <summary>
        /// Voxel Grid Filter: 공간을 격자로 분할하여 각 격자 내 점들을 대표점으로 치환
        /// 데이터 용량 축소 + 균일 밀도 확보
        /// </summary>
        /// <param name="source">원본 포인트 클라우드</param>
        /// <param name="voxelSize">격자 크기 (mm)</param>
        public static PointCloudData VoxelGridFilter(PointCloudData source, float voxelSize)
        {
            if (voxelSize <= 0)
                return source;

            int count = source.PointCount;
            float invVoxel = 1.0f / voxelSize;

            // 복셀 키 → (누적 좌표합, 누적 색상합, 점 수)
            var voxelMap = new Dictionary<(int, int, int), VoxelAccumulator>(count / 4);

            for (int i = 0; i < count; i++)
            {
                var p = source.Positions[i];

                // NaN/Inf 체크
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z))
                    continue;

                int vx = (int)MathF.Floor(p.X * invVoxel);
                int vy = (int)MathF.Floor(p.Y * invVoxel);
                int vz = (int)MathF.Floor(p.Z * invVoxel);
                var key = (vx, vy, vz);

                var c = source.Colors[i];

                if (voxelMap.TryGetValue(key, out var acc))
                {
                    acc.SumX += p.X;
                    acc.SumY += p.Y;
                    acc.SumZ += p.Z;
                    acc.SumR += c.R;
                    acc.SumG += c.G;
                    acc.SumB += c.B;
                    acc.Count++;
                }
                else
                {
                    voxelMap[key] = new VoxelAccumulator
                    {
                        SumX = p.X, SumY = p.Y, SumZ = p.Z,
                        SumR = c.R, SumG = c.G, SumB = c.B,
                        Count = 1
                    };
                }
            }

            int resultCount = voxelMap.Count;
            var positions = new Vector3[resultCount];
            var colors = new System.Windows.Media.Color[resultCount];
            int idx = 0;

            foreach (var acc in voxelMap.Values)
            {
                float inv = 1.0f / acc.Count;
                positions[idx] = new Vector3(acc.SumX * inv, acc.SumY * inv, acc.SumZ * inv);
                colors[idx] = System.Windows.Media.Color.FromRgb(
                    (byte)(acc.SumR / acc.Count),
                    (byte)(acc.SumG / acc.Count),
                    (byte)(acc.SumB / acc.Count));
                idx++;
            }

            return new PointCloudData
            {
                Name = source.Name + "_filtered",
                Positions = positions,
                Colors = colors,
                PointCount = resultCount,
                Intrinsics = source.Intrinsics
            };
        }

        /// <summary>
        /// SOR (Statistical Outlier Removal): 통계적으로 이상치 점 제거
        /// 각 점의 k-최근접 이웃 평균 거리를 계산하여, 평균 + n*표준편차 초과 점을 제거
        /// </summary>
        /// <param name="source">원본 포인트 클라우드</param>
        /// <param name="k">최근접 이웃 수 (기본 20)</param>
        /// <param name="stddevMultiplier">표준편차 배수 임계값 (기본 2.0)</param>
        public static PointCloudData StatisticalOutlierRemoval(PointCloudData source, int k = 20, double stddevMultiplier = 2.0)
        {
            int count = source.PointCount;
            if (count <= k)
                return source;

            // 각 점의 k-최근접 이웃 평균 거리 계산
            // 참고: 대용량 데이터에서는 KD-Tree가 필요하나, 여기서는 다운샘플 후 사용 가정
            var meanDistances = new double[count];

            Parallel.For(0, count, i =>
            {
                var pi = source.Positions[i];
                if (float.IsNaN(pi.X)) { meanDistances[i] = double.MaxValue; return; }

                // 간이 k-최근접 이웃: 전체 스캔 대신 stride 샘플링
                int stride = Math.Max(1, count / 10000); // 최대 10K 포인트 비교
                var distances = new List<float>(k * 2);

                for (int j = 0; j < count; j += stride)
                {
                    if (j == i) continue;
                    var pj = source.Positions[j];
                    if (float.IsNaN(pj.X)) continue;

                    float dist = Vector3.Distance(pi, pj);
                    distances.Add(dist);
                }

                if (distances.Count == 0) { meanDistances[i] = double.MaxValue; return; }

                distances.Sort();
                int kActual = Math.Min(k, distances.Count);
                double sum = 0;
                for (int n = 0; n < kActual; n++)
                    sum += distances[n];
                meanDistances[i] = sum / kActual;
            });

            // 전체 평균 및 표준편차
            double globalMean = 0;
            int validCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (meanDistances[i] < double.MaxValue)
                {
                    globalMean += meanDistances[i];
                    validCount++;
                }
            }
            if (validCount == 0) return source;
            globalMean /= validCount;

            double variance = 0;
            for (int i = 0; i < count; i++)
            {
                if (meanDistances[i] < double.MaxValue)
                {
                    double diff = meanDistances[i] - globalMean;
                    variance += diff * diff;
                }
            }
            double stddev = Math.Sqrt(variance / validCount);
            double threshold = globalMean + stddevMultiplier * stddev;

            // 임계값 이하인 점만 유지
            var keepIndices = new List<int>(validCount);
            for (int i = 0; i < count; i++)
            {
                if (meanDistances[i] <= threshold)
                    keepIndices.Add(i);
            }

            var positions = new Vector3[keepIndices.Count];
            var colors = new System.Windows.Media.Color[keepIndices.Count];
            for (int i = 0; i < keepIndices.Count; i++)
            {
                positions[i] = source.Positions[keepIndices[i]];
                colors[i] = source.Colors[keepIndices[i]];
            }

            return new PointCloudData
            {
                Name = source.Name + "_denoised",
                Positions = positions,
                Colors = colors,
                PointCount = keepIndices.Count,
                Intrinsics = source.Intrinsics
            };
        }

        /// <summary>
        /// ICP (Iterative Closest Point) 정합 — Point-to-Point 방식
        /// Source 포인트 클라우드를 Reference에 정합시키는 변환 행렬 계산
        /// </summary>
        /// <param name="reference">기준 포인트 클라우드 (고정)</param>
        /// <param name="source">정합할 포인트 클라우드 (이동)</param>
        /// <param name="maxIterations">최대 반복 횟수</param>
        /// <param name="tolerance">수렴 임계값 (mm)</param>
        /// <returns>Source를 Reference에 맞추는 변환 행렬</returns>
        public static Matrix4x4 ICP(PointCloudData reference, PointCloudData source,
            int maxIterations = 50, float tolerance = 0.01f)
            => ICP(reference, source, maxIterations, tolerance, out _);

        /// <summary>
        /// ICP + 실행 통계. 정합 품질 판단(수렴 여부·평균 오차)이 필요한 호출자용 —
        /// 기존 시그니처는 통계를 버리고 행렬만 반환한다.
        /// </summary>
        public static Matrix4x4 ICP(PointCloudData reference, PointCloudData source,
            int maxIterations, float tolerance, out IcpStats stats)
        {
            int srcCount = source.PointCount;
            int refCount = reference.PointCount;

            // 작업용 복사 (원본 보존)
            var srcPoints = new Vector3[srcCount];
            Array.Copy(source.Positions, srcPoints, srcCount);

            var accumulatedTransform = Matrix4x4.Identity;
            float lastMeanError = float.MaxValue;
            int iterationsRun = 0;
            bool converged = false;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                iterationsRun = iter + 1;
                // 1. Find closest points (brute-force, 다운샘플 후 사용 권장)
                var correspondences = FindClosestPoints(reference.Positions, refCount, srcPoints, srcCount);

                // 2. Compute centroid
                Vector3 srcCentroid = Vector3.Zero;
                Vector3 refCentroid = Vector3.Zero;
                int validPairs = 0;

                for (int i = 0; i < srcCount; i++)
                {
                    if (correspondences[i] < 0) continue;
                    srcCentroid += srcPoints[i];
                    refCentroid += reference.Positions[correspondences[i]];
                    validPairs++;
                }

                if (validPairs < 3) break;

                float invN = 1.0f / validPairs;
                srcCentroid *= invN;
                refCentroid *= invN;

                // 3. Compute cross-covariance matrix H (3x3)
                float h11 = 0, h12 = 0, h13 = 0;
                float h21 = 0, h22 = 0, h23 = 0;
                float h31 = 0, h32 = 0, h33 = 0;

                for (int i = 0; i < srcCount; i++)
                {
                    if (correspondences[i] < 0) continue;

                    var ps = srcPoints[i] - srcCentroid;
                    var pr = reference.Positions[correspondences[i]] - refCentroid;

                    h11 += ps.X * pr.X; h12 += ps.X * pr.Y; h13 += ps.X * pr.Z;
                    h21 += ps.Y * pr.X; h22 += ps.Y * pr.Y; h23 += ps.Y * pr.Z;
                    h31 += ps.Z * pr.X; h32 += ps.Z * pr.Y; h33 += ps.Z * pr.Z;
                }

                // 4. SVD via Jacobi iteration (3x3 특화)
                SvdDecompose3x3(
                    h11, h12, h13, h21, h22, h23, h31, h32, h33,
                    out var U, out var Vt);

                // R = V × U^T — U 는 SvdDecompose3x3 에서 정규직교 보장되므로
                // 역행렬 대신 전치 사용 (특이 행렬 Invert 실패 → NaN 오염 경로 제거)
                var Ut = Transpose3x3(U);
                var rotation = Vt * Ut;

                // det(R) < 0이면 반사 보정
                if (Matrix4x4Determinant3x3(rotation) < 0)
                {
                    // V의 3번째 열 부호 반전
                    Vt.M31 = -Vt.M31;
                    Vt.M32 = -Vt.M32;
                    Vt.M33 = -Vt.M33;
                    rotation = Vt * Ut;
                }

                // t = refCentroid - R × srcCentroid
                var rotatedSrcCentroid = Vector3.Transform(srcCentroid, rotation);
                var translation = refCentroid - rotatedSrcCentroid;

                // 변환 행렬 합성
                var stepTransform = rotation;
                stepTransform.M41 = translation.X;
                stepTransform.M42 = translation.Y;
                stepTransform.M43 = translation.Z;

                // 소스 점 업데이트
                for (int i = 0; i < srcCount; i++)
                {
                    srcPoints[i] = Vector3.Transform(srcPoints[i], stepTransform);
                }

                accumulatedTransform = accumulatedTransform * stepTransform;

                // 5. 수렴 체크: 평균 오차
                float totalError = 0;
                int errorCount = 0;
                for (int i = 0; i < srcCount; i++)
                {
                    if (correspondences[i] < 0) continue;
                    totalError += Vector3.Distance(srcPoints[i], reference.Positions[correspondences[i]]);
                    errorCount++;
                }

                float meanError = errorCount > 0 ? totalError / errorCount : float.MaxValue;
                lastMeanError = meanError;
                if (meanError < tolerance)
                {
                    converged = true;
                    break;
                }
            }

            stats = new IcpStats
            {
                MeanError = lastMeanError,
                Iterations = iterationsRun,
                Converged = converged
            };
            return accumulatedTransform;
        }

        /// <summary>
        /// ICP 실행 통계 — 정합 품질 판단용.
        /// </summary>
        public readonly struct IcpStats
        {
            /// <summary>마지막 반복의 대응점 평균 거리 (mm). 작을수록 잘 맞은 것.</summary>
            public float MeanError { get; init; }
            /// <summary>실제 수행된 반복 횟수.</summary>
            public int Iterations { get; init; }
            /// <summary>tolerance 이내로 수렴했는지. false 면 maxIterations 소진 또는 대응점 부족 중단.</summary>
            public bool Converged { get; init; }
        }

        /// <summary>
        /// PCA(주성분) 기반 거친 정렬 — ICP 앞단의 초기 자세 추정.
        /// 두 점군의 중심과 주성분 축을 맞추는 변환을 산출한다. PCA 축의 부호 모호성
        /// 때문에 프로퍼 회전 후보 4개를 만들고, 정렬 후 대응점 평균 거리가 최소인
        /// 후보를 선택한다. 초기 자세 차이가 커서 ICP 단독으로는 수렴하지 못하는
        /// 경우를 해결한다 (Mech-Vision 의 3D Coarse Matching 에 해당하는 간이안).
        /// 한계: 완전 대칭(구/정육면체) 물체는 주성분 축이 정의되지 않아 효과 없음.
        /// </summary>
        public static Matrix4x4 CoarseAlignPCA(PointCloudData reference, PointCloudData source)
        {
            int refCount = reference.PointCount;
            int srcCount = source.PointCount;
            if (refCount < 3 || srcCount < 3) return Matrix4x4.Identity;

            var refCentroid = ComputeCentroid(reference.Positions, refCount);
            var srcCentroid = ComputeCentroid(source.Positions, srcCount);

            var refAxes = PrincipalAxes(reference.Positions, refCount, refCentroid);
            var srcAxes = PrincipalAxes(source.Positions, srcCount, srcCentroid);

            // 후보 스코어링용 소스 샘플 (성능 — 최근접 탐색이 O(N·M))
            var samples = SamplePositions(source.Positions, srcCount, 2000);

            // PCA 축 부호 모호성 → 프로퍼 회전(det=+1)이 되는 부호 조합 4개
            var flips = new[]
            {
                new Vector3(1, 1, 1), new Vector3(1, -1, -1),
                new Vector3(-1, 1, -1), new Vector3(-1, -1, 1)
            };

            var best = Matrix4x4.Identity;
            float bestScore = float.MaxValue;

            foreach (var f in flips)
            {
                // 행벡터 규약: src 축(행렬 A 행) → 부호 적용된 ref 축(행렬 B 행) 매핑.
                // A·R = B, A 는 정규직교 → R = Aᵀ·B
                var a = AxesToMatrix(srcAxes[0], srcAxes[1], srcAxes[2]);
                var b = AxesToMatrix(refAxes[0] * f.X, refAxes[1] * f.Y, refAxes[2] * f.Z);
                var rotation = Matrix4x4.Transpose(a) * b;

                // t = refCentroid − srcCentroid·R
                var t = refCentroid - Vector3.Transform(srcCentroid, rotation);
                var candidate = rotation;
                candidate.M41 = t.X; candidate.M42 = t.Y; candidate.M43 = t.Z;

                float score = MeanNearestDistance(reference.Positions, refCount, samples, candidate);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 소스 각 점에서 기준 점군까지의 최근접 거리 (mm) — 편차(deviation) 분석용.
        /// 공간 그리드 해싱(셀 = maxSearchRadius)으로 O(N) 평균 — 전점 계산에서
        /// brute-force O(N·M) 을 회피. 거리는 maxSearchRadius 이내에서 정확하고,
        /// 그 밖이면 maxSearchRadius 로 클램프된다 (편차 판정 목적엔 충분 —
        /// 어차피 tolerance 초과).
        /// </summary>
        public static float[] ComputeNearestDistances(PointCloudData reference, PointCloudData source,
            float maxSearchRadius)
        {
            int refCount = reference.PointCount;
            int srcCount = source.PointCount;
            var distances = new float[srcCount];
            if (refCount == 0)
            {
                Array.Fill(distances, maxSearchRadius);
                return distances;
            }

            // 기준 점군을 셀 크기 = maxSearchRadius 그리드에 해싱 —
            // 이웃 27셀 탐색으로 반경 내 최근접이 보장됨
            float cell = Math.Max(maxSearchRadius, 1e-3f);
            var grid = new Dictionary<(int, int, int), List<int>>();
            for (int i = 0; i < refCount; i++)
            {
                var p = reference.Positions[i];
                if (float.IsNaN(p.X)) continue;
                var key = ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
                if (!grid.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    grid[key] = list;
                }
                list.Add(i);
            }

            System.Threading.Tasks.Parallel.For(0, srcCount, i =>
            {
                var sp = source.Positions[i];
                if (float.IsNaN(sp.X))
                {
                    distances[i] = maxSearchRadius;
                    return;
                }

                int cx = (int)MathF.Floor(sp.X / cell);
                int cy = (int)MathF.Floor(sp.Y / cell);
                int cz = (int)MathF.Floor(sp.Z / cell);

                float minSq = maxSearchRadius * maxSearchRadius;
                bool found = false;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var bucket)) continue;
                            foreach (var ri in bucket)
                            {
                                float d2 = Vector3.DistanceSquared(sp, reference.Positions[ri]);
                                if (d2 <= minSq)
                                {
                                    minSq = d2;
                                    found = true;
                                }
                            }
                        }

                distances[i] = found ? MathF.Sqrt(minSq) : maxSearchRadius;
            });

            return distances;
        }

        /// <summary>
        /// 정합 신뢰도 — 변환 적용 후 소스 점 중 최근접 기준점과의 거리가
        /// threshold(mm) 이내인 점의 비율 (0~1). Mech-Vision 의 matching
        /// score/confidence 에 해당하는 지표. 성능을 위해 최대 2000점 샘플링.
        /// </summary>
        public static float ComputeInlierRatio(PointCloudData reference, PointCloudData source,
            Matrix4x4 transform, float thresholdMm)
        {
            int refCount = reference.PointCount;
            int srcCount = source.PointCount;
            if (refCount == 0 || srcCount == 0) return 0f;

            var samples = SamplePositions(source.Positions, srcCount, 2000);
            var transformed = new Vector3[samples.Length];
            for (int i = 0; i < samples.Length; i++)
                transformed[i] = Vector3.Transform(samples[i], transform);

            var correspondences = FindClosestPoints(reference.Positions, refCount, transformed, transformed.Length);

            int inliers = 0, valid = 0;
            for (int i = 0; i < transformed.Length; i++)
            {
                if (correspondences[i] < 0) continue;
                valid++;
                if (Vector3.Distance(transformed[i], reference.Positions[correspondences[i]]) <= thresholdMm)
                    inliers++;
            }
            return valid > 0 ? (float)inliers / valid : 0f;
        }

        private static Vector3 ComputeCentroid(Vector3[] positions, int count)
        {
            Vector3 sum = Vector3.Zero;
            for (int i = 0; i < count; i++) sum += positions[i];
            return sum / count;
        }

        private static Vector3[] SamplePositions(Vector3[] positions, int count, int maxSamples)
        {
            if (count <= maxSamples)
            {
                var all = new Vector3[count];
                Array.Copy(positions, all, count);
                return all;
            }
            int stride = count / maxSamples;
            var samples = new Vector3[count / stride];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = positions[i * stride];
            return samples;
        }

        private static float MeanNearestDistance(Vector3[] refPositions, int refCount,
            Vector3[] srcSamples, Matrix4x4 transform)
        {
            var transformed = new Vector3[srcSamples.Length];
            for (int i = 0; i < srcSamples.Length; i++)
                transformed[i] = Vector3.Transform(srcSamples[i], transform);

            var correspondences = FindClosestPoints(refPositions, refCount, transformed, transformed.Length);

            float total = 0;
            int valid = 0;
            for (int i = 0; i < transformed.Length; i++)
            {
                if (correspondences[i] < 0) continue;
                total += Vector3.Distance(transformed[i], refPositions[correspondences[i]]);
                valid++;
            }
            return valid > 0 ? total / valid : float.MaxValue;
        }

        /// <summary>공분산 3x3의 고유벡터(주성분 축) — 고유값 내림차순, 단위 벡터.</summary>
        private static Vector3[] PrincipalAxes(Vector3[] positions, int count, Vector3 centroid)
        {
            // 공분산 (대칭 3x3)
            double c11 = 0, c12 = 0, c13 = 0, c22 = 0, c23 = 0, c33 = 0;
            for (int i = 0; i < count; i++)
            {
                var d = positions[i] - centroid;
                c11 += d.X * d.X; c12 += d.X * d.Y; c13 += d.X * d.Z;
                c22 += d.Y * d.Y; c23 += d.Y * d.Z; c33 += d.Z * d.Z;
            }

            var (values, vectors) = JacobiEigenSymmetric3x3(c11, c12, c13, c22, c23, c33);

            // 고유값 내림차순 정렬
            var order = new[] { 0, 1, 2 };
            Array.Sort(order, (x, y) => values[y].CompareTo(values[x]));
            var v0 = Vector3.Normalize(vectors[order[0]]);
            var v1 = Vector3.Normalize(vectors[order[1]]);
            var v2 = Vector3.Normalize(vectors[order[2]]);

            // 오른손 좌표계 강제 — 기준/현재 축 삼조의 손대칭이 어긋나면
            // 후보 회전이 전부 반사(det=-1)가 되어 올바른 자세가 후보에 없게 된다
            if (Vector3.Dot(Vector3.Cross(v0, v1), v2) < 0)
                v2 = -v2;

            return new[] { v0, v1, v2 };
        }

        /// <summary>대칭 3x3 행렬의 Jacobi 고유값 분해 — (고유값, 고유벡터) 반환.</summary>
        private static (double[] values, Vector3[] vectors) JacobiEigenSymmetric3x3(
            double a11, double a12, double a13, double a22, double a23, double a33)
        {
            var a = new[,] { { a11, a12, a13 }, { a12, a22, a23 }, { a13, a23, a33 } };
            var v = new[,] { { 1.0, 0, 0 }, { 0, 1.0, 0 }, { 0, 0, 1.0 } };

            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1e-12) break;

                for (int p = 0; p < 2; p++)
                {
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-15) continue;

                        double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
                        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        if (theta == 0) t = 1.0;
                        double c = 1.0 / Math.Sqrt(t * t + 1.0);
                        double s = t * c;

                        for (int k = 0; k < 3; k++)
                        {
                            double akp = a[k, p], akq = a[k, q];
                            a[k, p] = c * akp - s * akq;
                            a[k, q] = s * akp + c * akq;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double apk = a[p, k], aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk;
                            a[q, k] = s * apk + c * aqk;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double vkp = v[k, p], vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq;
                            v[k, q] = s * vkp + c * vkq;
                        }
                    }
                }
            }

            var values = new[] { a[0, 0], a[1, 1], a[2, 2] };
            var vectors = new[]
            {
                new Vector3((float)v[0, 0], (float)v[1, 0], (float)v[2, 0]),
                new Vector3((float)v[0, 1], (float)v[1, 1], (float)v[2, 1]),
                new Vector3((float)v[0, 2], (float)v[1, 2], (float)v[2, 2])
            };
            return (values, vectors);
        }

        /// <summary>행벡터 규약 — 세 축을 행으로 갖는 회전 행렬.</summary>
        private static Matrix4x4 AxesToMatrix(Vector3 row1, Vector3 row2, Vector3 row3)
        {
            return new Matrix4x4(
                row1.X, row1.Y, row1.Z, 0,
                row2.X, row2.Y, row2.Z, 0,
                row3.X, row3.Y, row3.Z, 0,
                0, 0, 0, 1);
        }

        /// <summary>
        /// 변환 행렬의 회전 성분을 오일러 각(도)으로 변환 — 결과 해석 편의용.
        /// X/Y/Z 축 회전량 (roll/pitch/yaw). 분해 실패 시 Zero.
        /// </summary>
        public static Vector3 ToEulerAnglesDegrees(Matrix4x4 m)
        {
            if (!Matrix4x4.Decompose(m, out _, out var q, out _))
                return Vector3.Zero;

            const float rad2Deg = 180f / MathF.PI;

            float sinrCosp = 2f * (q.W * q.X + q.Y * q.Z);
            float cosrCosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
            float rollX = MathF.Atan2(sinrCosp, cosrCosp);

            float sinp = Math.Clamp(2f * (q.W * q.Y - q.Z * q.X), -1f, 1f);
            float pitchY = MathF.Asin(sinp);

            float sinyCosp = 2f * (q.W * q.Z + q.X * q.Y);
            float cosyCosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
            float yawZ = MathF.Atan2(sinyCosp, cosyCosp);

            return new Vector3(rollX * rad2Deg, pitchY * rad2Deg, yawZ * rad2Deg);
        }

        #region ICP Internal

        private static int[] FindClosestPoints(Vector3[] refPoints, int refCount, Vector3[] srcPoints, int srcCount)
        {
            var correspondences = new int[srcCount];

            Parallel.For(0, srcCount, i =>
            {
                float minDist = float.MaxValue;
                int minIdx = -1;
                var sp = srcPoints[i];

                if (float.IsNaN(sp.X))
                {
                    correspondences[i] = -1;
                    return;
                }

                for (int j = 0; j < refCount; j++)
                {
                    var rp = refPoints[j];
                    if (float.IsNaN(rp.X)) continue;

                    float dx = sp.X - rp.X;
                    float dy = sp.Y - rp.Y;
                    float dz = sp.Z - rp.Z;
                    float dist = dx * dx + dy * dy + dz * dz;

                    if (dist < minDist)
                    {
                        minDist = dist;
                        minIdx = j;
                    }
                }

                correspondences[i] = minIdx;
            });

            return correspondences;
        }

        /// <summary>
        /// 간이 3x3 SVD (Jacobi 반복법 기반)
        /// H = U × S × V^T
        /// </summary>
        private static void SvdDecompose3x3(
            float h11, float h12, float h13,
            float h21, float h22, float h23,
            float h31, float h32, float h33,
            out Matrix4x4 U, out Matrix4x4 Vt)
        {
            // H^T × H → eigendecomposition으로 V 구하기
            // 여기서는 Power Iteration 대신 간이 Jacobi SVD 사용

            // H를 Matrix4x4로 패킹
            var H = new Matrix4x4(
                h11, h12, h13, 0,
                h21, h22, h23, 0,
                h31, h32, h33, 0,
                0, 0, 0, 1);

            // H^T × H
            var HtH = Transpose3x3(H) * H;

            // Jacobi eigendecomposition (simplified)
            var V = Matrix4x4.Identity;
            var A = HtH;

            for (int sweep = 0; sweep < 20; sweep++)
            {
                // 비대각 원소 중 가장 큰 것으로 회전
                JacobiRotation(ref A, ref V, 0, 1);
                JacobiRotation(ref A, ref V, 0, 2);
                JacobiRotation(ref A, ref V, 1, 2);
            }

            // Singular values = sqrt(eigenvalues)
            float s1 = MathF.Sqrt(MathF.Max(0, A.M11));
            float s2 = MathF.Sqrt(MathF.Max(0, A.M22));
            float s3 = MathF.Sqrt(MathF.Max(0, A.M33));

            // U = H × V × S^-1
            Vt = V;
            var Sinv = Matrix4x4.Identity;
            Sinv.M11 = s1 > 1e-10f ? 1.0f / s1 : 0;
            Sinv.M22 = s2 > 1e-10f ? 1.0f / s2 : 0;
            Sinv.M33 = s3 > 1e-10f ? 1.0f / s3 : 0;

            U = H * V * Sinv;

            // H 가 rank-deficient 면(대응점 coplanar/colinear) 특이값 0 → U 에 영 열이
            // 생겨 이후 회전 계산이 NaN 으로 오염된다. Gram-Schmidt + 외적으로 열을
            // 보완해 항상 정규직교 행렬을 보장한다.
            OrthonormalizeColumns3x3(ref U);
        }

        /// <summary>3x3 부분의 열벡터를 정규직교화 — 영/퇴화 열은 외적으로 보완.</summary>
        private static void OrthonormalizeColumns3x3(ref Matrix4x4 m)
        {
            var c1 = new Vector3(m.M11, m.M21, m.M31);
            var c2 = new Vector3(m.M12, m.M22, m.M32);

            c1 = c1.LengthSquared() > 1e-12f ? Vector3.Normalize(c1) : Vector3.UnitX;

            c2 -= Vector3.Dot(c2, c1) * c1;
            if (c2.LengthSquared() > 1e-12f)
            {
                c2 = Vector3.Normalize(c2);
            }
            else
            {
                // c1 과 평행/영 — c1 에 수직인 임의 축 선택
                var helper = MathF.Abs(c1.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
                c2 = Vector3.Normalize(Vector3.Cross(c1, helper));
            }

            var c3 = Vector3.Cross(c1, c2);

            m.M11 = c1.X; m.M21 = c1.Y; m.M31 = c1.Z;
            m.M12 = c2.X; m.M22 = c2.Y; m.M32 = c2.Z;
            m.M13 = c3.X; m.M23 = c3.Y; m.M33 = c3.Z;
        }

        private static Matrix4x4 Transpose3x3(Matrix4x4 m)
        {
            return new Matrix4x4(
                m.M11, m.M21, m.M31, 0,
                m.M12, m.M22, m.M32, 0,
                m.M13, m.M23, m.M33, 0,
                0, 0, 0, 1);
        }

        private static void JacobiRotation(ref Matrix4x4 A, ref Matrix4x4 V, int p, int q)
        {
            float app = GetElement(A, p, p);
            float aqq = GetElement(A, q, q);
            float apq = GetElement(A, p, q);

            if (MathF.Abs(apq) < 1e-10f) return;

            float tau = (aqq - app) / (2.0f * apq);
            // tau=0 이면 Sign(0)=0 → t=0 으로 회전이 멈추는 정지 버그 — 45° 회전이 정답
            float t = tau == 0f
                ? 1f
                : MathF.Sign(tau) / (MathF.Abs(tau) + MathF.Sqrt(1 + tau * tau));
            float c = 1.0f / MathF.Sqrt(1 + t * t);
            float s = t * c;

            // Givens rotation
            var G = Matrix4x4.Identity;
            SetElement(ref G, p, p, c);
            SetElement(ref G, q, q, c);
            SetElement(ref G, p, q, s);
            SetElement(ref G, q, p, -s);

            A = Transpose3x3(G) * A * G;
            V = V * G;
        }

        private static float GetElement(Matrix4x4 m, int row, int col)
        {
            return (row, col) switch
            {
                (0, 0) => m.M11, (0, 1) => m.M12, (0, 2) => m.M13,
                (1, 0) => m.M21, (1, 1) => m.M22, (1, 2) => m.M23,
                (2, 0) => m.M31, (2, 1) => m.M32, (2, 2) => m.M33,
                _ => 0
            };
        }

        private static void SetElement(ref Matrix4x4 m, int row, int col, float value)
        {
            switch (row, col)
            {
                case (0, 0): m.M11 = value; break;
                case (0, 1): m.M12 = value; break;
                case (0, 2): m.M13 = value; break;
                case (1, 0): m.M21 = value; break;
                case (1, 1): m.M22 = value; break;
                case (1, 2): m.M23 = value; break;
                case (2, 0): m.M31 = value; break;
                case (2, 1): m.M32 = value; break;
                case (2, 2): m.M33 = value; break;
            }
        }

        private static float Matrix4x4Determinant3x3(Matrix4x4 m)
        {
            return m.M11 * (m.M22 * m.M33 - m.M23 * m.M32)
                 - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31)
                 + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
        }

        #endregion

        #region Euclidean Clustering

        /// <summary>
        /// 유클리드 거리 기반 점군 클러스터링.
        /// 거리 tolerance 이내 점들을 BFS로 연결 → 클러스터 형성.
        /// 그리드 해싱 (voxel = tolerance)으로 인접 이웃만 검사해 O(N) 평균 복잡도.
        /// </summary>
        /// <param name="source">입력 점군</param>
        /// <param name="tolerance">동일 클러스터 판정 거리 (mm)</param>
        /// <param name="minPoints">최소 클러스터 크기 (이하 무시 → 노이즈)</param>
        /// <param name="maxPoints">최대 클러스터 크기 (이상 무시 → 배경 큰 덩어리 제외)</param>
        /// <returns>크기 내림차순 정렬된 클러스터 목록 (각각 PointCloudData)</returns>
        public static List<PointCloudData> EuclideanClustering(
            PointCloudData source, float tolerance, int minPoints, int maxPoints)
        {
            int n = source.PointCount;
            var result = new List<PointCloudData>();
            if (n == 0 || tolerance <= 0) return result;

            var positions = source.Positions;
            var colors = source.Colors;
            bool hasColors = colors != null && colors.Length >= n;

            // 그리드 해싱
            float invTol = 1f / tolerance;
            var grid = new Dictionary<(int, int, int), List<int>>(n / 4);
            for (int i = 0; i < n; i++)
            {
                var p = positions[i];
                var key = (
                    (int)System.Math.Floor(p.X * invTol),
                    (int)System.Math.Floor(p.Y * invTol),
                    (int)System.Math.Floor(p.Z * invTol));
                if (!grid.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(8);
                    grid[key] = bucket;
                }
                bucket.Add(i);
            }

            var visited = new bool[n];
            var clusters = new List<List<int>>();
            var queue = new Queue<int>(128);
            float tolSq = tolerance * tolerance;

            for (int seed = 0; seed < n; seed++)
            {
                if (visited[seed]) continue;

                var cluster = new List<int>(64);
                queue.Clear();
                queue.Enqueue(seed);
                visited[seed] = true;

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    cluster.Add(idx);
                    var p = positions[idx];

                    int cx = (int)System.Math.Floor(p.X * invTol);
                    int cy = (int)System.Math.Floor(p.Y * invTol);
                    int cz = (int)System.Math.Floor(p.Z * invTol);

                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var bucket)) continue;
                        for (int k = 0; k < bucket.Count; k++)
                        {
                            int nIdx = bucket[k];
                            if (visited[nIdx]) continue;
                            var q = positions[nIdx];
                            float ddx = p.X - q.X;
                            float ddy = p.Y - q.Y;
                            float ddz = p.Z - q.Z;
                            if (ddx * ddx + ddy * ddy + ddz * ddz <= tolSq)
                            {
                                visited[nIdx] = true;
                                queue.Enqueue(nIdx);
                            }
                        }
                    }
                }

                if (cluster.Count >= minPoints && cluster.Count <= maxPoints)
                    clusters.Add(cluster);
            }

            // 크기 내림차순 정렬
            clusters.Sort((a, b) => b.Count.CompareTo(a.Count));

            // 인덱스 리스트 → PointCloudData
            for (int ci = 0; ci < clusters.Count; ci++)
            {
                var indices = clusters[ci];
                var clusterPositions = new Vector3[indices.Count];
                System.Windows.Media.Color[] clusterColors =
                    hasColors ? new System.Windows.Media.Color[indices.Count] : System.Array.Empty<System.Windows.Media.Color>();

                for (int j = 0; j < indices.Count; j++)
                {
                    clusterPositions[j] = positions[indices[j]];
                    if (hasColors) clusterColors[j] = colors![indices[j]];
                }

                result.Add(new PointCloudData
                {
                    Name = $"Cluster_{ci}",
                    Positions = clusterPositions,
                    Colors = clusterColors,
                    PointCount = indices.Count,
                    Intrinsics = source.Intrinsics
                });
            }
            return result;
        }

        /// <summary>
        /// 여러 PointCloudData를 위치/색상 배열로 단순 병합 (그리드 메타 무시).
        /// </summary>
        public static PointCloudData ConcatenateClouds(IList<PointCloudData> clouds, string name = "Merged")
        {
            int total = 0;
            bool anyColors = false;
            for (int i = 0; i < clouds.Count; i++)
            {
                total += clouds[i].PointCount;
                if (clouds[i].Colors != null && clouds[i].Colors.Length >= clouds[i].PointCount)
                    anyColors = true;
            }
            var positions = new Vector3[total];
            var colors = anyColors ? new System.Windows.Media.Color[total] : System.Array.Empty<System.Windows.Media.Color>();
            int offset = 0;
            for (int i = 0; i < clouds.Count; i++)
            {
                var c = clouds[i];
                int count = c.PointCount;
                System.Array.Copy(c.Positions, 0, positions, offset, count);
                if (anyColors && c.Colors != null && c.Colors.Length >= count)
                    System.Array.Copy(c.Colors, 0, colors, offset, count);
                offset += count;
            }
            return new PointCloudData
            {
                Name = name,
                Positions = positions,
                Colors = colors,
                PointCount = total,
                Intrinsics = clouds.Count > 0 ? clouds[0].Intrinsics : null
            };
        }

        #endregion
    }

    /// <summary>
    /// Voxel 누적 데이터 (좌표 합, 색상 합, 점 수)
    /// </summary>
    internal class VoxelAccumulator
    {
        public float SumX, SumY, SumZ;
        public int SumR, SumG, SumB;
        public int Count;
    }
}
