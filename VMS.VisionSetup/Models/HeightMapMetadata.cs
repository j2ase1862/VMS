using System.Numerics;
using OpenCvSharp;

namespace VMS.VisionSetup.Models
{
    public class HeightMapMetadata
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public float ZReference { get; set; }
        public float ZMin { get; set; }
        public float ZMax { get; set; }

        /// <summary>
        /// CV_32FC1 float depth map 참조 (pos.Z - zRef 원시값 보존)
        /// </summary>
        public Mat? DepthMap32F { get; set; }

        /// <summary>
        /// Per-pixel 3D coordinate lookup (index = row * Width + col).
        /// Null entry means the pixel had no valid 3D point.
        /// </summary>
        public Vector3?[] PixelTo3D { get; set; } = Array.Empty<Vector3?>();

        public Vector3? GetPoint3D(int u, int v)
        {
            if (u < 0 || u >= Width || v < 0 || v >= Height)
                return null;
            return PixelTo3D[v * Width + u];
        }

        /// <summary>
        /// 서브픽셀 바이리니어 보간으로 Z값 반환 (Caliper 등 결과 3D 복원용)
        /// DepthMap32F가 있으면 float map에서 보간, 없으면 PixelTo3D 폴백
        /// </summary>
        public float? GetInterpolatedZ(float u, float v)
        {
            if (DepthMap32F != null && !DepthMap32F.IsDisposed && !DepthMap32F.Empty())
            {
                return InterpolateFromMat(DepthMap32F, u, v);
            }

            // Fallback to PixelTo3D (nearest neighbor)
            int ix = (int)Math.Round(u);
            int iy = (int)Math.Round(v);
            var pt = GetPoint3D(ix, iy);
            return pt?.Z;
        }

        /// <summary>
        /// CV_32FC1 Mat에서 바이리니어 보간
        /// </summary>
        private static unsafe float? InterpolateFromMat(Mat mat, float u, float v)
        {
            int w = mat.Width;
            int h = mat.Height;

            // 범위 밖이면 null
            if (u < 0 || u >= w - 1 || v < 0 || v >= h - 1)
            {
                // 경계 내 정수 좌표 폴백
                int ix = (int)Math.Round(u);
                int iy = (int)Math.Round(v);
                if (ix < 0 || ix >= w || iy < 0 || iy >= h)
                    return null;
                return ((float*)mat.Data)[iy * w + ix];
            }

            int x0 = (int)u;
            int y0 = (int)v;
            float fx = u - x0;
            float fy = v - y0;

            float* ptr = (float*)mat.Data;
            float v00 = ptr[y0 * w + x0];
            float v10 = ptr[y0 * w + x0 + 1];
            float v01 = ptr[(y0 + 1) * w + x0];
            float v11 = ptr[(y0 + 1) * w + x0 + 1];

            float top = v00 + (v10 - v00) * fx;
            float bottom = v01 + (v11 - v01) * fx;
            return top + (bottom - top) * fy;
        }
    }
}
