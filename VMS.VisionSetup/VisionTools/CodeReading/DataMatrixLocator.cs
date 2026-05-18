using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace VMS.VisionSetup.VisionTools.CodeReading
{
    /// <summary>
    /// DataMatrix 후보 영역 검출기.
    /// adaptive threshold + morph close로 모듈을 단일 blob으로 융합 → contour 휴리스틱으로
    /// 정사각형/고밀도 영역을 후보로 반환. ZXing의 L-finder 검출이 잡음에 약한 경우의 fallback.
    /// 텍스트/잡음이 섞인 넓은 ROI에서 DM 위치를 먼저 좁힌 뒤 영역별 디코딩하면 인식률이 크게 향상됨.
    /// </summary>
    public static class DataMatrixLocator
    {
        /// <summary>
        /// 후보 bbox 목록 반환 (입력 이미지 좌표계).
        /// </summary>
        /// <param name="image">그레이/컬러 입력 (컬러는 자동 변환).</param>
        /// <param name="minSide">후보의 최소 한 변 길이 (px). 너무 작은 노이즈 제거용.</param>
        /// <param name="maxSideFraction">이미지 짧은 변 대비 최대 후보 변 비율 (0~1). 배경 영역 흡수 방지.</param>
        public static List<Rect> FindCandidates(Mat image, int minSide = 30, double maxSideFraction = 0.7)
        {
            if (image == null || image.Empty()) return new List<Rect>();

            Mat gray;
            bool ownsGray = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                ownsGray = true;
            }
            else gray = image;

            try
            {
                // 1) 적응형 이진화 (불균일 조명 대응). BinaryInv: 어두운 모듈을 흰색으로.
                int shortSide = Math.Min(gray.Width, gray.Height);
                int blockSize = Math.Max(11, (shortSide / 30) | 1);
                using var bin = new Mat();
                Cv2.AdaptiveThreshold(gray, bin, 255,
                    AdaptiveThresholdTypes.MeanC, ThresholdTypes.BinaryInv, blockSize, 5);

                // 2) 모폴로지 close — 모듈 사이 갭을 메워 DM 전체를 단일 blob으로
                using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
                using var closed = new Mat();
                Cv2.MorphologyEx(bin, closed, MorphTypes.Close, kernel, iterations: 2);

                // 3) 외곽 contour 추출
                Cv2.FindContours(closed, out Point[][] contours, out _,
                    RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                int maxSide = (int)(shortSide * maxSideFraction);
                var raw = new List<Rect>();

                foreach (var contour in contours)
                {
                    if (contour.Length < 4) continue;
                    var bbox = Cv2.BoundingRect(contour);

                    // 크기 필터
                    if (bbox.Width < minSide || bbox.Height < minSide) continue;
                    if (bbox.Width > maxSide || bbox.Height > maxSide) continue;

                    // 가로세로 비 — DM은 정사각형 (회전/원근으로 약간 어긋날 수 있음)
                    double aspect = (double)bbox.Width / bbox.Height;
                    if (aspect < 0.5 || aspect > 2.0) continue;

                    // Solidity (블록처럼 가득 찬 모양 우선)
                    double area = Cv2.ContourArea(contour);
                    var hull = Cv2.ConvexHull(contour);
                    double hullArea = Cv2.ContourArea(hull);
                    if (hullArea < 1 || area / hullArea < 0.7) continue;

                    // bbox 내부 채움률 (텍스트 같은 sparse 영역 배제)
                    using var sub = new Mat(closed, bbox);
                    double fillRatio = (double)Cv2.CountNonZero(sub) / (bbox.Width * bbox.Height);
                    if (fillRatio < 0.5) continue;

                    // 디코딩 마진 패딩 (Quiet Zone 보장)
                    int padX = Math.Max(4, bbox.Width / 7);
                    int padY = Math.Max(4, bbox.Height / 7);
                    int x = Math.Max(0, bbox.X - padX);
                    int y = Math.Max(0, bbox.Y - padY);
                    int w = Math.Min(gray.Width - x, bbox.Width + 2 * padX);
                    int h = Math.Min(gray.Height - y, bbox.Height + 2 * padY);
                    raw.Add(new Rect(x, y, w, h));
                }

                return MergeOverlapping(raw);
            }
            finally
            {
                if (ownsGray) gray.Dispose();
            }
        }

        /// <summary>
        /// 후보가 50% 이상 겹치면 union으로 병합 (DM 분리된 contour 합치기).
        /// </summary>
        private static List<Rect> MergeOverlapping(List<Rect> rects)
        {
            var result = new List<Rect>();
            var used = new bool[rects.Count];
            for (int i = 0; i < rects.Count; i++)
            {
                if (used[i]) continue;
                var merged = rects[i];
                used[i] = true;
                bool grew;
                do
                {
                    grew = false;
                    for (int j = 0; j < rects.Count; j++)
                    {
                        if (used[j]) continue;
                        var inter = merged.Intersect(rects[j]);
                        int interArea = inter.Width * inter.Height;
                        int minArea = Math.Min(merged.Width * merged.Height,
                                               rects[j].Width * rects[j].Height);
                        if (minArea > 0 && interArea > minArea / 2)
                        {
                            merged = merged.Union(rects[j]);
                            used[j] = true;
                            grew = true;
                        }
                    }
                } while (grew);
                result.Add(merged);
            }
            return result;
        }
    }
}
