using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using OpenCvSharp;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// MMF에 프레임을 직렬화하여 쓰는 Writer (VMS 메인 앱용).
    /// Dispose 시 WriterAlive 이벤트를 리셋하여 Reader에게 종료를 알림.
    /// </summary>
    public sealed class SharedFrameWriter : IDisposable
    {
        private MemoryMappedFile? _mmf;
        private Mutex? _mutex;
        private EventWaitHandle? _frameReadyEvent;
        private EventWaitHandle? _writerAliveEvent;
        private long _frameCounter;
        private bool _disposed;

        /// <summary>
        /// MMF 및 동기화 객체 생성. 앱 시작 시 한 번 호출.
        /// </summary>
        public void Initialize()
        {
            _mmf = MemoryMappedFile.CreateOrOpen(
                SharedFrameConstants.MmfName,
                SharedFrameConstants.MmfCapacity);

            _mutex = new Mutex(false, SharedFrameConstants.MutexName);

            _frameReadyEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
                SharedFrameConstants.FrameReadyEventName);

            _writerAliveEvent = new EventWaitHandle(false, EventResetMode.ManualReset,
                SharedFrameConstants.WriterAliveEventName);

            _writerAliveEvent.Set();
        }

        /// <summary>
        /// AcquisitionResult를 MMF에 직렬화.
        /// Mutex를 100ms 내에 획득하지 못하면 프레임 드롭 (카메라 루프 차단 방지).
        /// </summary>
        public void WriteFrame(AcquisitionResult result)
        {
            if (_disposed || _mmf == null || _mutex == null) return;

            bool acquired = false;
            try
            {
                acquired = _mutex.WaitOne(100);
                if (!acquired) return; // 프레임 드롭

                using var accessor = _mmf.CreateViewAccessor(0, SharedFrameConstants.MmfCapacity);

                long offset = 0;

                // ── DataFlags 결정 ──
                uint flags = 0;
                if (result.Image2D != null && !result.Image2D.Empty())
                    flags |= SharedFrameConstants.FlagHas2D;
                if (result.PointCloud != null && result.PointCloud.PointCount > 0)
                    flags |= SharedFrameConstants.FlagHas3D;

                var counter = Interlocked.Increment(ref _frameCounter);

                // ── 2D 이미지 정보 ──
                int imgW = 0, imgH = 0, imgC = 0, imgStride = 0;
                if ((flags & SharedFrameConstants.FlagHas2D) != 0)
                {
                    var mat = result.Image2D!;
                    imgW = mat.Width;
                    imgH = mat.Height;
                    imgC = mat.Channels();
                    imgStride = (int)mat.Step();
                }

                // ── 3D 포인트클라우드 정보 ──
                int ptCount = 0, gridW = 0, gridH = 0;
                byte[] nameBytes = Array.Empty<byte>();
                if ((flags & SharedFrameConstants.FlagHas3D) != 0)
                {
                    var pc = result.PointCloud!;
                    ptCount = pc.PointCount;
                    gridW = pc.GridWidth;
                    gridH = pc.GridHeight;
                    nameBytes = Encoding.UTF8.GetBytes(pc.Name ?? "PointCloud");
                }

                // ── 바디 크기 검증 ──
                long bodySize = SharedFrameConstants.HeaderSize;
                if ((flags & SharedFrameConstants.FlagHas2D) != 0)
                    bodySize += (long)imgStride * imgH;
                if ((flags & SharedFrameConstants.FlagHas3D) != 0)
                    bodySize += nameBytes.Length + (long)ptCount * 12 + (long)ptCount * 4;

                if (bodySize > SharedFrameConstants.MmfCapacity)
                    return; // 용량 초과 시 스킵

                // ── 헤더 쓰기 (64B) ──
                accessor.Write(SharedFrameConstants.OffsetMagic, SharedFrameConstants.Magic);
                accessor.Write(SharedFrameConstants.OffsetVersion, SharedFrameConstants.Version);
                accessor.Write(SharedFrameConstants.OffsetDataFlags, flags);
                accessor.Write(SharedFrameConstants.OffsetTimestamp, DateTime.UtcNow.Ticks);
                accessor.Write(SharedFrameConstants.OffsetFrameCounter, counter);
                accessor.Write(SharedFrameConstants.OffsetImageWidth, imgW);
                accessor.Write(SharedFrameConstants.OffsetImageHeight, imgH);
                accessor.Write(SharedFrameConstants.OffsetImageChannels, imgC);
                accessor.Write(SharedFrameConstants.OffsetImageStride, imgStride);
                accessor.Write(SharedFrameConstants.OffsetPointCount, ptCount);
                accessor.Write(SharedFrameConstants.OffsetGridWidth, gridW);
                accessor.Write(SharedFrameConstants.OffsetGridHeight, gridH);
                accessor.Write(SharedFrameConstants.OffsetNameLengthBytes, nameBytes.Length);
                accessor.Write(SharedFrameConstants.OffsetReserved, 0);

                offset = SharedFrameConstants.HeaderSize;

                // ── 2D 이미지 바디 ──
                if ((flags & SharedFrameConstants.FlagHas2D) != 0)
                {
                    var mat = result.Image2D!;
                    int totalBytes = imgStride * imgH;
                    byte[] buffer = new byte[totalBytes];
                    Marshal.Copy(mat.Data, buffer, 0, totalBytes);
                    accessor.WriteArray(offset, buffer, 0, totalBytes);
                    offset += totalBytes;
                }

                // ── 3D 포인트클라우드 바디 ──
                if ((flags & SharedFrameConstants.FlagHas3D) != 0)
                {
                    var pc = result.PointCloud!;

                    // Name (UTF8 bytes)
                    accessor.WriteArray(offset, nameBytes, 0, nameBytes.Length);
                    offset += nameBytes.Length;

                    // Positions (float×3 per point)
                    var posFloats = new float[ptCount * 3];
                    for (int i = 0; i < ptCount; i++)
                    {
                        posFloats[i * 3] = pc.Positions[i].X;
                        posFloats[i * 3 + 1] = pc.Positions[i].Y;
                        posFloats[i * 3 + 2] = pc.Positions[i].Z;
                    }
                    var posBytes = new byte[ptCount * 12];
                    Buffer.BlockCopy(posFloats, 0, posBytes, 0, posBytes.Length);
                    accessor.WriteArray(offset, posBytes, 0, posBytes.Length);
                    offset += posBytes.Length;

                    // Colors (RGBA 4 bytes per point)
                    var colorBytes = new byte[ptCount * 4];
                    for (int i = 0; i < ptCount; i++)
                    {
                        colorBytes[i * 4] = pc.Colors[i].R;
                        colorBytes[i * 4 + 1] = pc.Colors[i].G;
                        colorBytes[i * 4 + 2] = pc.Colors[i].B;
                        colorBytes[i * 4 + 3] = pc.Colors[i].A;
                    }
                    accessor.WriteArray(offset, colorBytes, 0, colorBytes.Length);
                }

                // ── 새 프레임 알림 ──
                _frameReadyEvent?.Set();
            }
            catch (AbandonedMutexException)
            {
                // Reader가 비정상 종료한 경우 — Mutex 재획득됨, 계속 진행
            }
            finally
            {
                if (acquired)
                    _mutex?.ReleaseMutex();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _writerAliveEvent?.Reset();
            _writerAliveEvent?.Dispose();
            _frameReadyEvent?.Dispose();
            _mutex?.Dispose();
            _mmf?.Dispose();
        }
    }
}
