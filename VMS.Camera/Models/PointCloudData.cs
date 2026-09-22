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

        private DepthIntrinsics? _intrinsics;
        /// <summary>
        /// 뎁스 카메라 내부 파라미터 — grab 시 설정 (.vpc 로드·공유 프레임 등은 null).
        /// X/Y 픽셀 좌표를 mm로 환산하는 도구(PointCloudCluster 자동 치수 등)가 사용.
        /// 좌표를 회전/변환하면 픽셀 공간이 깨지므로 정합(Registration) 결과에는 전달하지 않는다.
        /// </summary>
        public DepthIntrinsics? Intrinsics
        {
            get => _intrinsics;
            set => SetProperty(ref _intrinsics, value);
        }

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
        //              + (선택) 후행 블록 — 아래 TrailerSignature 참조
        private static readonly byte[] FileSignature = "VPC1"u8.ToArray();

        // ── 후행 블록 (2026-09-23 신설) ─────────────────────────────────
        //
        // 색상 뒤에 이어 붙이는 선택 블록. **헤더를 바꾸지 않은 이유는 호환성**이다 —
        // 구버전 로더는 색상까지만 읽고 스트림 끝을 확인하지 않으므로 뒤에 붙은 블록을
        // 무시하고 정상 로드한다(새 파일 → 구버전 앱 OK). 새 로더는 후행 블록이 없는
        // 옛 파일도 그대로 읽는다.
        //
        // 무엇을 싣는가: **뎁스 카메라 내부 파라미터(fx/fy/cx/cy)**. grab 점군의 X/Y 는
        // 뎁스맵 화소이고 Z 만 mm 인데, 화소를 mm 로 바꾸는 fx/fy 가 파일에 없어서 저장본을
        // 다시 불러오면 치수 자동 환산(PointCloudCluster 의 AutoFromCamera)이 꺼졌다.
        // 즉 **저장한 점군으로는 XY 치수를 mm 로 잴 수 없었다** — 오프라인 검증이나
        // 시험 자료로 쓰기 어려운 제약이었고, 그래서 배율을 따로 받아 적어야 했다.
        private static readonly byte[] TrailerSignature = "VPCX"u8.ToArray();
        private const int TrailerVersion = 1;

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

            // 후행 블록 — 내부 파라미터가 있을 때만 (없으면 옛 파일과 바이트가 동일하다)
            var intrinsics = Intrinsics;
            if (intrinsics is { IsValid: true })
            {
                bw.Write(TrailerSignature);
                bw.Write(TrailerVersion);
                bw.Write(intrinsics.Fx);
                bw.Write(intrinsics.Fy);
                bw.Write(intrinsics.Cx);
                bw.Write(intrinsics.Cy);
            }
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
                GridHeight = gridHeight,
                Intrinsics = ReadTrailerIntrinsics(br)
            };
        }

        /// <summary>
        /// 색상 뒤의 후행 블록에서 내부 파라미터를 읽는다. 없으면(옛 파일) null.
        /// 깨진 꼬리는 <b>로드 실패로 만들지 않는다</b> — 점군 본문은 이미 온전히 읽었고,
        /// 여기서 던지면 멀쩡한 파일이 안 열리는 것처럼 보인다. 내부 파라미터만 포기한다.
        /// </summary>
        private static DepthIntrinsics? ReadTrailerIntrinsics(BinaryReader br)
        {
            try
            {
                var stream = br.BaseStream;
                // 서명(4) + 버전(4) + double 4개(32)
                if (!stream.CanSeek || stream.Length - stream.Position < 40)
                    return null;

                var sig = br.ReadBytes(4);
                if (sig.Length < 4 || sig[0] != TrailerSignature[0] || sig[1] != TrailerSignature[1] ||
                    sig[2] != TrailerSignature[2] || sig[3] != TrailerSignature[3])
                    return null;

                int version = br.ReadInt32();
                if (version != TrailerVersion)
                    return null;   // 더 새로운 형식 — 본문만 쓰고 넘어간다

                var intrinsics = new DepthIntrinsics
                {
                    Fx = br.ReadDouble(),
                    Fy = br.ReadDouble(),
                    Cx = br.ReadDouble(),
                    Cy = br.ReadDouble()
                };
                return intrinsics.IsValid ? intrinsics : null;
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException)
            {
                return null;
            }
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
