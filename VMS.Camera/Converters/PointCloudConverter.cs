using System.Numerics;
using OpenCvSharp;
using VMS.Camera.Models;

namespace VMS.Camera.Converters
{
    /// <summary>
    /// 3D 포인트 클라우드를 2D Depth Map(Mat)으로 변환하는 유틸리티
    /// 높이 축: pos.Z (PointCloudViewer/DepthMapViewer와 일치)
    /// </summary>
    public static class PointCloudConverter
    {
        /// <summary>
        /// 포인트 클라우드 → CV_32FC1 float depth map (원시 높이값 보존)
        /// 각 픽셀 값 = pos.Z - zRef (기준면 대비 상대 높이)
        /// </summary>
        public static Mat ToDepthMap32F(PointCloudData pointCloud, float zRef)
        {
            if (!pointCloud.IsOrganized)
                throw new InvalidOperationException("Depth map requires an organized (grid) point cloud.");

            int w = pointCloud.GridWidth;
            int h = pointCloud.GridHeight;
            var depthMap = new Mat(h, w, MatType.CV_32FC1, Scalar.All(0));

            unsafe
            {
                float* ptr = (float*)depthMap.Data;
                var positions = pointCloud.Positions;

                for (int i = 0; i < w * h; i++)
                {
                    ptr[i] = positions[i].Z - zRef;
                }
            }

            return depthMap;
        }

        /// <summary>
        /// 포인트 클라우드 → CV_8UC1 정규화 depth map (표시/일반 도구용)
        /// Z값을 [zMin, zMax] 범위에서 [0, 255]로 정규화
        /// </summary>
        public static Mat ToDepthMap8U(PointCloudData pointCloud, float zRef, float zMin, float zMax)
        {
            if (!pointCloud.IsOrganized)
                throw new InvalidOperationException("Depth map requires an organized (grid) point cloud.");

            int w = pointCloud.GridWidth;
            int h = pointCloud.GridHeight;
            float range = zMax - zMin;
            if (range <= 0) range = 1f;

            var heightMap = new Mat(h, w, MatType.CV_8UC1, Scalar.All(0));

            unsafe
            {
                byte* ptr = (byte*)heightMap.Data;
                var positions = pointCloud.Positions;

                for (int i = 0; i < w * h; i++)
                {
                    float normalizedZ = positions[i].Z - zRef;
                    float t = (normalizedZ - zMin) / range;
                    t = Math.Clamp(t, 0f, 1f);
                    ptr[i] = (byte)(t * 255f);
                }
            }

            return heightMap;
        }

        /// <summary>
        /// unorganized(비격자) 점군 → 정사투영(orthographic) CV_32FC1 depth map.
        /// Registration/Cluster 등을 거치면 격자 구조가 깨지는데, 그 점군도
        /// 2D 검사 도구에서 쓸 수 있도록 XY 평면 격자에 비닝한다.
        /// 셀당 최고 높이(pos.Z 최대 = 윗면) 채택, 빈 셀 = 0. 각 픽셀 값 = Z - zRef.
        /// 셀 크기는 점 밀도 기반 자동(점당 평균 면적의 제곱근), 긴 변 64~2048px 클램프.
        /// 픽셀↔3D 역참조 테이블(PixelTo3D)도 함께 생성해 2D 결과의 3D 복원 지원.
        /// </summary>
        public static (Mat DepthMap32F, Vector3?[] PixelTo3D, int Width, int Height)
            OrthographicToDepthMap32F(PointCloudData pointCloud, float zRef)
        {
            int count = pointCloud.PointCount;
            var positions = pointCloud.Positions;
            if (count == 0)
                throw new InvalidOperationException("Point cloud is empty.");

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            int valid = 0;
            for (int i = 0; i < count; i++)
            {
                var p = positions[i];
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z)) continue;
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
                valid++;
            }
            if (valid < 4 || maxX <= minX || maxY <= minY)
                throw new InvalidOperationException("Not enough valid points for orthographic projection.");

            float extentX = maxX - minX;
            float extentY = maxY - minY;

            // 점 밀도 기반 셀 크기 → 해상도. 긴 변 64~2048 클램프
            float cell = MathF.Sqrt(extentX * extentY / valid);
            float longExtent = MathF.Max(extentX, extentY);
            if (cell <= 0 || longExtent / cell > 2048f) cell = longExtent / 2048f;
            if (longExtent / cell < 64f) cell = longExtent / 64f;

            int w = Math.Max(1, (int)MathF.Ceiling(extentX / cell));
            int h = Math.Max(1, (int)MathF.Ceiling(extentY / cell));

            var depthMap = new Mat(h, w, MatType.CV_32FC1, Scalar.All(0));
            var pixelTo3D = new Vector3?[w * h];
            var bestZ = new float[w * h];
            Array.Fill(bestZ, float.MinValue);

            unsafe
            {
                float* ptr = (float*)depthMap.Data;
                for (int i = 0; i < count; i++)
                {
                    var p = positions[i];
                    if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z)) continue;

                    int u = Math.Min(w - 1, (int)((p.X - minX) / cell));
                    int v = Math.Min(h - 1, (int)((p.Y - minY) / cell));
                    int idx = v * w + u;

                    // 같은 셀에 여러 점 — 윗면(Z 최대) 우선
                    if (p.Z > bestZ[idx])
                    {
                        bestZ[idx] = p.Z;
                        ptr[idx] = p.Z - zRef;
                        pixelTo3D[idx] = p;
                    }
                }
            }

            return (depthMap, pixelTo3D, w, h);
        }

        /// <summary>
        /// CV_32FC1 float depth map → CV_8UC1 정규화 변환
        /// </summary>
        public static Mat DepthMap32FTo8U(Mat depthMap32F, float zMin, float zMax)
        {
            if (depthMap32F.Type() != MatType.CV_32FC1)
                throw new ArgumentException("Input must be CV_32FC1.", nameof(depthMap32F));

            int w = depthMap32F.Width;
            int h = depthMap32F.Height;
            float range = zMax - zMin;
            if (range <= 0) range = 1f;

            var result = new Mat(h, w, MatType.CV_8UC1, Scalar.All(0));

            unsafe
            {
                float* srcPtr = (float*)depthMap32F.Data;
                byte* dstPtr = (byte*)result.Data;

                for (int i = 0; i < w * h; i++)
                {
                    float t = (srcPtr[i] - zMin) / range;
                    t = Math.Clamp(t, 0f, 1f);
                    dstPtr[i] = (byte)(t * 255f);
                }
            }

            return result;
        }

        /// <summary>
        /// CV_32FC1 float depth map → CV_8UC1 필터링 변환
        /// 범위 밖 값은 0(검정) 처리, 범위 내 값만 [0, 255] 정규화
        /// </summary>
        public static Mat DepthMap32FTo8UFiltered(Mat depthMap32F, float zMin, float zMax)
        {
            if (depthMap32F.Type() != MatType.CV_32FC1)
                throw new ArgumentException("Input must be CV_32FC1.", nameof(depthMap32F));

            int w = depthMap32F.Width;
            int h = depthMap32F.Height;
            float range = zMax - zMin;
            if (range <= 0) range = 1f;

            var result = new Mat(h, w, MatType.CV_8UC1, Scalar.All(0));

            unsafe
            {
                float* srcPtr = (float*)depthMap32F.Data;
                byte* dstPtr = (byte*)result.Data;

                for (int i = 0; i < w * h; i++)
                {
                    float val = srcPtr[i];
                    if (val < zMin || val > zMax)
                    {
                        dstPtr[i] = 0;
                    }
                    else
                    {
                        float t = (val - zMin) / range;
                        dstPtr[i] = (byte)(t * 255f);
                    }
                }
            }

            return result;
        }
    }
}
