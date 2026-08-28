using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using OpenCvSharp;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// MMF에서 프레임을 역직렬화하여 읽는 Reader (VMS.VisionSetup용).
    /// Writer 생존 확인, 프레임 대기, deep copy 반환.
    /// </summary>
    public sealed class SharedFrameReader : IDisposable
    {
        private MemoryMappedFile? _mmf;
        private Mutex? _mutex;
        private EventWaitHandle? _frameReadyEvent;
        private EventWaitHandle? _writerAliveEvent;
        private EventWaitHandle? _readerAliveEvent;
        private long _lastFrameCounter;
        private bool _disposed;

        /// <summary>
        /// Writer가 생성한 MMF/동기화 객체를 OpenExisting으로 연결.
        /// Writer가 없으면 false 반환.
        /// </summary>
        public bool TryConnect()
        {
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(SharedFrameConstants.MmfName);
                _mutex = Mutex.OpenExisting(SharedFrameConstants.MutexName);
                _frameReadyEvent = EventWaitHandle.OpenExisting(SharedFrameConstants.FrameReadyEventName);
                _writerAliveEvent = EventWaitHandle.OpenExisting(SharedFrameConstants.WriterAliveEventName);

                // Reader 존재를 Writer 에게 알린다 — Writer 는 이 이벤트가 없으면
                // 프레임 직렬화를 통째로 건너뛴다 (메인 화면 단독 라이브 시 페이지파일
                // I/O 제거). 이 프로세스가 죽으면 핸들이 닫히며 커널 객체도 소멸하므로
                // 별도 해제 누락 걱정 없이 Writer 쪽에서 자동 감지된다.
                _readerAliveEvent = new EventWaitHandle(
                    true, EventResetMode.ManualReset, SharedFrameConstants.ReaderAliveEventName);
                _readerAliveEvent.Set();
                return true;
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// VMS 메인 앱 실행 여부 — 연결 없이 WriterAlive 이벤트만 프로브.
        /// VMS 메인은 시작 시 SharedFrameWriter.Initialize 에서 이벤트를 Set 하고
        /// 종료 시 리셋하므로, VisionSetup 이 카메라 소유권 중재(직접 연결 차단)
        /// 판단에 사용할 수 있다.
        /// </summary>
        public static bool IsVmsMainRunning()
        {
            try
            {
                if (!EventWaitHandle.TryOpenExisting(SharedFrameConstants.WriterAliveEventName, out var handle))
                    return false;
                using (handle)
                {
                    return handle.WaitOne(0);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Writer 생존 확인 (비차단)
        /// </summary>
        public bool IsWriterAlive
        {
            get
            {
                if (_writerAliveEvent == null) return false;
                try
                {
                    return _writerAliveEvent.WaitOne(0);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 새 프레임이 준비될 때까지 대기.
        /// </summary>
        public bool WaitForFrame(int timeoutMs = 1000)
        {
            if (_frameReadyEvent == null) return false;
            try
            {
                return _frameReadyEvent.WaitOne(timeoutMs);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// MMF에서 프레임 읽기 (deep copy).
        /// skipIfSameFrame=true면 이전과 동일한 FrameCounter일 때 null 반환.
        /// </summary>
        public SharedFrameData? TryReadFrame(bool skipIfSameFrame = true)
        {
            if (_disposed || _mmf == null || _mutex == null) return null;

            bool acquired = false;
            try
            {
                acquired = _mutex.WaitOne(1000);
                if (!acquired) return null;

                using var accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                // ── 헤더 검증 ──
                uint magic = accessor.ReadUInt32(SharedFrameConstants.OffsetMagic);
                if (magic != SharedFrameConstants.Magic) return null;

                uint version = accessor.ReadUInt32(SharedFrameConstants.OffsetVersion);
                if (version != SharedFrameConstants.Version) return null;

                uint flags = accessor.ReadUInt32(SharedFrameConstants.OffsetDataFlags);
                long timestamp = accessor.ReadInt64(SharedFrameConstants.OffsetTimestamp);
                long frameCounter = accessor.ReadInt64(SharedFrameConstants.OffsetFrameCounter);

                if (skipIfSameFrame && frameCounter == _lastFrameCounter)
                    return null;

                int imgW = accessor.ReadInt32(SharedFrameConstants.OffsetImageWidth);
                int imgH = accessor.ReadInt32(SharedFrameConstants.OffsetImageHeight);
                int imgC = accessor.ReadInt32(SharedFrameConstants.OffsetImageChannels);
                int imgStride = accessor.ReadInt32(SharedFrameConstants.OffsetImageStride);
                int ptCount = accessor.ReadInt32(SharedFrameConstants.OffsetPointCount);
                int gridW = accessor.ReadInt32(SharedFrameConstants.OffsetGridWidth);
                int gridH = accessor.ReadInt32(SharedFrameConstants.OffsetGridHeight);
                int nameLenBytes = accessor.ReadInt32(SharedFrameConstants.OffsetNameLengthBytes);

                // 카메라 식별자 (v2 헤더 고정 슬롯) — 범위를 벗어난 길이는 손상으로 보고 버린다
                int cameraIdLen = accessor.ReadInt32(SharedFrameConstants.OffsetCameraIdLengthBytes);
                string cameraId = string.Empty;
                if (cameraIdLen > 0 && cameraIdLen <= SharedFrameConstants.CameraIdMaxBytes)
                {
                    var cameraIdBytes = new byte[cameraIdLen];
                    accessor.ReadArray(SharedFrameConstants.OffsetCameraId, cameraIdBytes, 0, cameraIdLen);
                    cameraId = Encoding.UTF8.GetString(cameraIdBytes);
                }

                long offset = SharedFrameConstants.HeaderSize;
                var data = new SharedFrameData
                {
                    FrameCounter = frameCounter,
                    TimestampTicks = timestamp,
                    CameraId = cameraId
                };

                // ── 2D 이미지 복원 ──
                if ((flags & SharedFrameConstants.FlagHas2D) != 0 && imgW > 0 && imgH > 0 && imgC > 0)
                {
                    int totalBytes = imgStride * imgH;
                    var buffer = new byte[totalBytes];
                    accessor.ReadArray(offset, buffer, 0, totalBytes);
                    offset += totalBytes;

                    var matType = imgC switch
                    {
                        1 => MatType.CV_8UC1,
                        3 => MatType.CV_8UC3,
                        4 => MatType.CV_8UC4,
                        _ => MatType.CV_8UC3
                    };

                    var mat = new Mat(imgH, imgW, matType);
                    Marshal.Copy(buffer, 0, mat.Data, totalBytes);
                    data.Image2D = mat;
                }

                // ── 3D 포인트클라우드 복원 ──
                if ((flags & SharedFrameConstants.FlagHas3D) != 0 && ptCount > 0)
                {
                    // Name
                    var nameBytes = new byte[nameLenBytes];
                    if (nameLenBytes > 0)
                        accessor.ReadArray(offset, nameBytes, 0, nameLenBytes);
                    offset += nameLenBytes;
                    string name = nameLenBytes > 0 ? Encoding.UTF8.GetString(nameBytes) : "PointCloud";

                    // Positions
                    var posBytes = new byte[ptCount * 12];
                    accessor.ReadArray(offset, posBytes, 0, posBytes.Length);
                    offset += posBytes.Length;

                    var posFloats = new float[ptCount * 3];
                    Buffer.BlockCopy(posBytes, 0, posFloats, 0, posBytes.Length);

                    var positions = new Vector3[ptCount];
                    for (int i = 0; i < ptCount; i++)
                    {
                        positions[i] = new Vector3(
                            posFloats[i * 3],
                            posFloats[i * 3 + 1],
                            posFloats[i * 3 + 2]);
                    }

                    // Colors (RGBA)
                    var colorBytes = new byte[ptCount * 4];
                    accessor.ReadArray(offset, colorBytes, 0, colorBytes.Length);

                    var colors = new System.Windows.Media.Color[ptCount];
                    for (int i = 0; i < ptCount; i++)
                    {
                        colors[i] = System.Windows.Media.Color.FromArgb(
                            colorBytes[i * 4 + 3],
                            colorBytes[i * 4],
                            colorBytes[i * 4 + 1],
                            colorBytes[i * 4 + 2]);
                    }

                    data.PointCloud = new PointCloudData
                    {
                        Name = name,
                        Positions = positions,
                        Colors = colors,
                        GridWidth = gridW,
                        GridHeight = gridH
                    };
                }

                _lastFrameCounter = frameCounter;
                return data;
            }
            catch (AbandonedMutexException)
            {
                return null;
            }
            finally
            {
                if (acquired)
                    _mutex?.ReleaseMutex();
            }
        }

        private void Disconnect()
        {
            _readerAliveEvent?.Reset();
            _readerAliveEvent?.Dispose();
            _readerAliveEvent = null;
            _writerAliveEvent?.Dispose();
            _writerAliveEvent = null;
            _frameReadyEvent?.Dispose();
            _frameReadyEvent = null;
            _mutex?.Dispose();
            _mutex = null;
            _mmf?.Dispose();
            _mmf = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
        }
    }
}
