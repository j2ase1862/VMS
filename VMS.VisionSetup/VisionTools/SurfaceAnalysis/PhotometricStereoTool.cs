using VMS.VisionSetup.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VMS.VisionSetup.VisionTools.SurfaceAnalysis
{
    /// <summary>
    /// 포토메트릭 스테레오 — 조명 방향별 N장(최소 3장)으로 표면 법선/알베도/결함강조 복원.
    /// Lambertian 모델 I = ρ(n·l) 을 픽셀별 최소제곱으로 풀어 표면 미세 결함(스크래치·덴트·각인)을 부각.
    ///
    /// A 방식: 각 조명 샘플의 ImagePath 에서 이미지를 로드. 경로가 비면 파이프라인 입력(inputImage)을 사용.
    ///   → B 방식(카메라 멀티프레임 grab)으로 확장 시 LoadLightImages() 한 곳만 교체하면 됨.
    /// </summary>
    public class PhotometricStereoTool : VisionToolBase
    {
        /// <summary>조명 1개 = 방향벡터(Lx,Ly,Lz) + 이미지 경로(A 방식).</summary>
        public ObservableCollection<LightSample> Lights { get; set; } = new();

        private PsOutputType _outputType = PsOutputType.DefectEnhanced;
        public PsOutputType OutputType
        {
            get => _outputType;
            set => SetProperty(ref _outputType, value);
        }

        // 결함강조 출력 시 곡률(법선 발산) 강조 계수
        private double _curvatureGain = 8.0;
        public double CurvatureGain
        {
            get => _curvatureGain;
            set => SetProperty(ref _curvatureGain, value);
        }

        // 그림자/정반사 픽셀 제거 임계 (0~255). 해당 조명에서 너무 어둡거나 포화된 값은 법선 추정에서 신뢰 저하 처리.
        private int _shadowThreshold = 10;
        public int ShadowThreshold
        {
            get => _shadowThreshold;
            set => SetProperty(ref _shadowThreshold, value);
        }

        private int _highlightThreshold = 245;
        public int HighlightThreshold
        {
            get => _highlightThreshold;
            set => SetProperty(ref _highlightThreshold, value);
        }

        public PhotometricStereoTool()
        {
            Name = "Photometric Stereo";
            ToolType = "PhotometricStereoTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();
            var loaded = new List<Mat>();
            var fimgs = new List<Mat>();
            Mat? L = null, Lpinv = null, normalMap = null, albedo = null;
            try
            {
                // ── 1. 조명 이미지 N장 로드 (A 방식) ──
                var (imgs, dirs) = LoadLightImages(inputImage);
                loaded = imgs;
                int n = imgs.Count;
                if (n < 3)
                    throw new InvalidOperationException(
                        $"포토메트릭 스테레오는 조명 3개 이상 필요 (현재 {n}개)");

                // 모든 조명 이미지는 동일 크기여야 함
                int rows = imgs[0].Rows, cols = imgs[0].Cols;
                for (int i = 1; i < n; i++)
                    if (imgs[i].Rows != rows || imgs[i].Cols != cols)
                        throw new InvalidOperationException("조명 이미지 크기가 서로 다릅니다.");

                // ── 2. 조명 방향 행렬 L(n×3)과 의사역행렬 Lpinv = (LᵀL)⁻¹Lᵀ(3×n) ──
                L = new Mat(n, 3, MatType.CV_64F);
                for (int i = 0; i < n; i++)
                {
                    var d = Normalize(dirs[i]);
                    L.Set<double>(i, 0, d.Item0);
                    L.Set<double>(i, 1, d.Item1);
                    L.Set<double>(i, 2, d.Item2);
                }
                Lpinv = (L.T() * L).Inv() * L.T();

                // ── 3. 그레이 float 변환 ──
                foreach (var m in imgs)
                {
                    Mat g = m.Channels() == 1 ? m : m.CvtColor(ColorConversionCodes.BGR2GRAY);
                    Mat f = new Mat();
                    g.ConvertTo(f, MatType.CV_32F);
                    fimgs.Add(f);
                    if (g != m) g.Dispose();
                }

                // ── 4. 픽셀별 법선/알베도 ──
                (normalMap, albedo) = ComputeNormals(fimgs, Lpinv, n);

                // ── 5. 출력 선택 ──
                Mat output = OutputType switch
                {
                    PsOutputType.NormalMap => EncodeNormalMap(normalMap),
                    PsOutputType.Albedo => NormalizeTo8U(albedo),
                    PsOutputType.DefectEnhanced => DefectEnhance(normalMap, CurvatureGain),
                    _ => DefectEnhance(normalMap, CurvatureGain)
                };

                result.Success = true;
                result.Message = $"PS 완료 (조명 {n}개, {OutputType})";
                result.OutputImage = output;
                result.Data["LightCount"] = n;
                result.Data["OutputType"] = OutputType.ToString();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"포토메트릭 스테레오 실패: {ex.Message}";
            }
            finally
            {
                foreach (var f in fimgs) f.Dispose();
                foreach (var m in loaded) m.Dispose();
                L?.Dispose();
                Lpinv?.Dispose();
                normalMap?.Dispose();
                albedo?.Dispose();
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        /// <summary>
        /// ★ B 방식 전환 지점: 여기만 카메라 멀티프레임 버퍼 읽기로 교체하면 inline grab 지원.
        /// 반환: (조명 이미지 목록, 대응하는 조명 방향벡터 목록).
        /// </summary>
        private (List<Mat>, List<Vec3d>) LoadLightImages(Mat inputImage)
        {
            var imgs = new List<Mat>();
            var dirs = new List<Vec3d>();
            foreach (var s in Lights)
            {
                Mat m = string.IsNullOrWhiteSpace(s.ImagePath)
                    ? inputImage.Clone()
                    : Cv2.ImRead(s.ImagePath, ImreadModes.Unchanged);
                if (m.Empty()) { m.Dispose(); continue; }
                imgs.Add(m);
                dirs.Add(new Vec3d(s.Lx, s.Ly, s.Lz));
            }
            return (imgs, dirs);
        }

        /// <summary>픽셀별 g = Lpinv·I, ρ=|g|, n=g/ρ. 그림자/정반사 픽셀은 신뢰 저하 → 평면 법선(0,0,1).</summary>
        private (Mat normalMap, Mat albedo) ComputeNormals(List<Mat> fimgs, Mat lpinv, int n)
        {
            int rows = fimgs[0].Rows, cols = fimgs[0].Cols;

            // 3채널 법선은 단일채널 3장으로 채운 뒤 Merge (float 인덱서만 사용 → 안정적)
            var nxM = new Mat(rows, cols, MatType.CV_32FC1);
            var nyM = new Mat(rows, cols, MatType.CV_32FC1);
            var nzM = new Mat(rows, cols, MatType.CV_32FC1);
            var albM = new Mat(rows, cols, MatType.CV_32FC1);

            var inIdx = new Mat.Indexer<float>[n];
            for (int i = 0; i < n; i++) inIdx[i] = fimgs[i].GetGenericIndexer<float>();
            var nx = nxM.GetGenericIndexer<float>();
            var ny = nyM.GetGenericIndexer<float>();
            var nz = nzM.GetGenericIndexer<float>();
            var alb = albM.GetGenericIndexer<float>();

            // Lpinv(3×n)을 배열로 캐싱 (At<double> 호출 비용 제거)
            var lp = new double[3, n];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < n; c++) lp[r, c] = lpinv.At<double>(r, c);

            int shadow = ShadowThreshold, highlight = HighlightThreshold;

            Parallel.For(0, rows, y =>
            {
                for (int x = 0; x < cols; x++)
                {
                    double gx = 0, gy = 0, gz = 0;
                    int validCount = 0;
                    for (int i = 0; i < n; i++)
                    {
                        float I = inIdx[i][y, x];
                        if (I > shadow && I < highlight) validCount++;
                        gx += lp[0, i] * I;
                        gy += lp[1, i] * I;
                        gz += lp[2, i] * I;
                    }
                    double mag = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                    alb[y, x] = (float)mag;

                    // 유효 조명이 3개 미만이면(대부분 그림자/포화) 법선 추정 불안정 → 평면 처리
                    if (mag > 1e-6 && validCount >= 3)
                    {
                        nx[y, x] = (float)(gx / mag);
                        ny[y, x] = (float)(gy / mag);
                        nz[y, x] = (float)(gz / mag);
                    }
                    else
                    {
                        nx[y, x] = 0f; ny[y, x] = 0f; nz[y, x] = 1f;
                    }
                }
            });

            var normalMap = new Mat();
            Cv2.Merge(new[] { nxM, nyM, nzM }, normalMap);
            nxM.Dispose(); nyM.Dispose(); nzM.Dispose();
            return (normalMap, albM);
        }

        /// <summary>법선(-1~1) → 0~255 RGB 인코딩 (시각화/디버깅용).</summary>
        private Mat EncodeNormalMap(Mat normalMap)
        {
            Mat shifted = (normalMap + new Scalar(1, 1, 1)) * 127.5;
            Mat outImg = new Mat();
            shifted.ConvertTo(outImg, MatType.CV_8UC3);
            shifted.Dispose();
            return outImg;
        }

        /// <summary>결함강조: 법선장의 발산(∂nx/∂x + ∂ny/∂y) ≈ 평균곡률 → 미세 형상변화 부각.</summary>
        private Mat DefectEnhance(Mat normalMap, double gain)
        {
            Mat[] ch = normalMap.Split();
            Mat gx = new Mat(), gy = new Mat();
            Cv2.Sobel(ch[0], gx, MatType.CV_32F, 1, 0, 3);   // ∂nx/∂x
            Cv2.Sobel(ch[1], gy, MatType.CV_32F, 0, 1, 3);   // ∂ny/∂y
            Mat curvature = (gx + gy) * gain;
            Mat outImg = NormalizeTo8U(curvature);
            foreach (var c in ch) c.Dispose();
            gx.Dispose(); gy.Dispose(); curvature.Dispose();
            return outImg;
        }

        private Mat NormalizeTo8U(Mat src)
        {
            Mat norm = new Mat(), outImg = new Mat();
            Cv2.Normalize(src, norm, 0, 255, NormTypes.MinMax);
            norm.ConvertTo(outImg, MatType.CV_8U);
            norm.Dispose();
            return outImg;
        }

        private static Vec3d Normalize(Vec3d v)
        {
            double m = Math.Sqrt(v.Item0 * v.Item0 + v.Item1 * v.Item1 + v.Item2 * v.Item2);
            return m < 1e-9 ? new Vec3d(0, 0, 1) : new Vec3d(v.Item0 / m, v.Item1 / m, v.Item2 / m);
        }

        public override VisionToolBase Clone()
        {
            var clone = new PhotometricStereoTool
            {
                Name = Name,
                ToolType = ToolType,
                IsEnabled = IsEnabled,
                ROI = ROI,
                UseROI = UseROI,
                OutputType = OutputType,
                CurvatureGain = CurvatureGain,
                ShadowThreshold = ShadowThreshold,
                HighlightThreshold = HighlightThreshold
            };
            foreach (var l in Lights)
                clone.Lights.Add(new LightSample
                {
                    Lx = l.Lx,
                    Ly = l.Ly,
                    Lz = l.Lz,
                    ImagePath = l.ImagePath
                });
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }

    public enum PsOutputType
    {
        NormalMap,
        Albedo,
        DefectEnhanced
    }

    /// <summary>조명 1개의 방향벡터 + 이미지 경로(A 방식). 바인딩되므로 ObservableObject.</summary>
    public class LightSample : ObservableObject
    {
        private double _lx;
        private double _ly;
        private double _lz = 1;
        private string _imagePath = "";

        public double Lx { get => _lx; set => SetProperty(ref _lx, value); }
        public double Ly { get => _ly; set => SetProperty(ref _ly, value); }
        public double Lz { get => _lz; set => SetProperty(ref _lz, value); }
        public string ImagePath { get => _imagePath; set => SetProperty(ref _imagePath, value); }
    }
}
