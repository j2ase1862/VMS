using CommunityToolkit.Mvvm.ComponentModel;
using System.Buffers;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace VMS.Camera.Models
{
    public class PointCloudData : ObservableObject, IDisposable
    {
        private bool _isPooled;
        private bool _disposed;

        private string _name = "PointCloud";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private Vector3[] _positions = Array.Empty<Vector3>();
        public Vector3[] Positions
        {
            get => _positions;
            set => SetProperty(ref _positions, value);
        }

        private System.Windows.Media.Color[] _colors = Array.Empty<System.Windows.Media.Color>();
        public System.Windows.Media.Color[] Colors
        {
            get => _colors;
            set => SetProperty(ref _colors, value);
        }

        private int _gridWidth;
        public int GridWidth
        {
            get => _gridWidth;
            set => SetProperty(ref _gridWidth, value);
        }

        private int _gridHeight;
        public int GridHeight
        {
            get => _gridHeight;
            set => SetProperty(ref _gridHeight, value);
        }

        /// <summary>
        /// 실제 포인트 수. ArrayPool 사용 시 배열 길이 > 실제 개수일 수 있음.
        /// </summary>
        private int _pointCount;
        public int PointCount
        {
            get => _pointCount > 0 ? _pointCount : Positions.Length;
            set => SetProperty(ref _pointCount, value);
        }

        public bool IsOrganized => GridWidth > 0 && GridHeight > 0 && GridWidth * GridHeight == PointCount;

        /// <summary>
        /// ArrayPool에서 배열을 빌려 생성 (GC 부하 최소화)
        /// </summary>
        public static PointCloudData CreatePooled(int count, string name = "PointCloud", int gridWidth = 0, int gridHeight = 0)
        {
            return new PointCloudData
            {
                Name = name,
                Positions = ArrayPool<Vector3>.Shared.Rent(count),
                Colors = ArrayPool<System.Windows.Media.Color>.Shared.Rent(count),
                _pointCount = count,
                GridWidth = gridWidth,
                GridHeight = gridHeight,
                _isPooled = true
            };
        }

        public static PointCloudData FromArrays(float[] xyz, byte[]? rgb = null, string name = "PointCloud", int width = 0, int height = 0)
        {
            int count = xyz.Length / 3;
            var positions = new Vector3[count];
            var colors = new System.Windows.Media.Color[count];

            for (int i = 0; i < count; i++)
            {
                positions[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);

                if (rgb != null && rgb.Length >= (i + 1) * 3)
                {
                    colors[i] = System.Windows.Media.Color.FromRgb(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
                }
                else
                {
                    colors[i] = System.Windows.Media.Color.FromRgb(255, 255, 255);
                }
            }

            return new PointCloudData
            {
                Name = name,
                Positions = positions,
                Colors = colors,
                GridWidth = width,
                GridHeight = height
            };
        }

        #region Binary Save / Load (.vpc format)

        // File format: VPC1 header + metadata + positions (float×3) + colors (byte×3)
        private static readonly byte[] FileSignature = "VPC1"u8.ToArray();

        /// <summary>
        /// 포인트 클라우드를 바이너리 파일로 저장 (.vpc)
        /// </summary>
        public void SaveToFile(string filePath)
        {
            int count = PointCount;
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
            using var bw = new BinaryWriter(fs);

            // Header
            bw.Write(FileSignature);       // 4 bytes magic
            bw.Write(count);               // 4 bytes point count
            bw.Write(GridWidth);           // 4 bytes
            bw.Write(GridHeight);          // 4 bytes
            bw.Write(Name ?? "PointCloud"); // length-prefixed string

            // Positions — bulk write as float triplets
            var posBuffer = new byte[count * 12]; // 3 floats × 4 bytes
            for (int i = 0; i < count; i++)
            {
                var p = Positions[i];
                int offset = i * 12;
                MemoryMarshal.Write(posBuffer.AsSpan(offset), in p.X);
                MemoryMarshal.Write(posBuffer.AsSpan(offset + 4), in p.Y);
                MemoryMarshal.Write(posBuffer.AsSpan(offset + 8), in p.Z);
            }
            bw.Write(posBuffer);

            // Colors — RGB bytes
            var colorBuffer = new byte[count * 3];
            for (int i = 0; i < count; i++)
            {
                var c = Colors[i];
                int offset = i * 3;
                colorBuffer[offset] = c.R;
                colorBuffer[offset + 1] = c.G;
                colorBuffer[offset + 2] = c.B;
            }
            bw.Write(colorBuffer);
        }

        /// <summary>
        /// 바이너리 파일에서 포인트 클라우드 로드 (.vpc)
        /// </summary>
        public static PointCloudData LoadFromFile(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
            using var br = new BinaryReader(fs);

            // Header
            var sig = br.ReadBytes(4);
            if (sig.Length < 4 || sig[0] != FileSignature[0] || sig[1] != FileSignature[1] ||
                sig[2] != FileSignature[2] || sig[3] != FileSignature[3])
                throw new InvalidDataException("Invalid VPC file format.");

            int count = br.ReadInt32();
            int gridWidth = br.ReadInt32();
            int gridHeight = br.ReadInt32();
            string name = br.ReadString();

            // Positions
            var posBuffer = br.ReadBytes(count * 12);
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * 12;
                float x = MemoryMarshal.Read<float>(posBuffer.AsSpan(offset));
                float y = MemoryMarshal.Read<float>(posBuffer.AsSpan(offset + 4));
                float z = MemoryMarshal.Read<float>(posBuffer.AsSpan(offset + 8));
                positions[i] = new Vector3(x, y, z);
            }

            // Colors
            var colorBuffer = br.ReadBytes(count * 3);
            var colors = new System.Windows.Media.Color[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * 3;
                colors[i] = System.Windows.Media.Color.FromRgb(
                    colorBuffer[offset], colorBuffer[offset + 1], colorBuffer[offset + 2]);
            }

            return new PointCloudData
            {
                Name = name,
                Positions = positions,
                Colors = colors,
                GridWidth = gridWidth,
                GridHeight = gridHeight
            };
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_isPooled)
            {
                if (Positions.Length > 0)
                    ArrayPool<Vector3>.Shared.Return(Positions);
                if (Colors.Length > 0)
                    ArrayPool<System.Windows.Media.Color>.Shared.Return(Colors);

                Positions = Array.Empty<Vector3>();
                Colors = Array.Empty<System.Windows.Media.Color>();
            }
        }
    }
}
