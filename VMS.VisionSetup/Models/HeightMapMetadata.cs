using System.Numerics;

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
    }
}
