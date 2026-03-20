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
