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
        private MemoryMappedViewAccessor? _accessor;
        private Mutex? _mutex;
        private EventWaitHandle? _frameReadyEvent;
        private EventWaitHandle? _writerAliveEvent;
        private long _frameCounter;
        private bool _disposed;

        // Reader 존재 프로브 캐시 — 커널 객체 조회를 프레임마다 하지 않는다.
        // 초기값은 long.MinValue 가 아니라 -Interval — MinValue 는 (now - tick)
        // 뺄셈이 오버플로해 음수가 되어 첫 프로브가 영원히 스킵된다.
        private long _lastReaderProbeTick = -ReaderProbeIntervalMs;
        private bool _lastReaderProbeResult;
        private const long ReaderProbeIntervalMs = 1000;

        // 프레임 복사 버퍼 재사용(grow-only) — 매 프레임 수십 MB LOH 할당이
        // GC·커밋 압박으로 번지는 것을 막는다 (Mutex 보유 구간에서만 접근)
        private byte[] _frameBuffer = Array.Empty<byte>();

        /// <summary>
        /// MMF 및 동기화 객체 생성. 앱 시작 시 한 번 호출.
        /// </summary>
        public void Initialize()
        {
            _mmf = MemoryMappedFile.CreateOrOpen(
                SharedFrameConstants.MmfName,
                SharedFrameConstants.MmfCapacity);

            // 뷰는 한 번만 매핑해 재사용한다. 매 프레임 CreateViewAccessor/Dispose 를
            // 반복하면 프레임이 더럽힌 페이지가 매번 워킹셋에서 수정 페이지 목록으로
            // 넘어가 Windows 가 페이지파일에 상시 flush — 라이브 중 디스크 사용률이
            // 프레임레이트×프레임크기만큼 치솟는다 (세연공장 2026-08-29, PC 전체 멈춤).
            _accessor = _mmf.CreateViewAccessor(0, SharedFrameConstants.MmfCapacity);

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
        /// <summary>
        /// Reader(VisionSetup)가 붙어 있는지 확인 — 없으면 WriteFrame 은 아무 것도 하지
        /// 않는다. 메인 화면 단독 라이브에서 프레임 직렬화(대형 복사 + MMF 더티 페이지
        /// → 페이지파일 I/O) 비용이 통째로 사라진다. Reader 가 비정상 종료하면 핸들이
        /// 닫히며 커널 객체가 소멸하므로 TryOpenExisting 실패 → 자동으로 다시 꺼진다.
        /// </summary>
        private bool HasReader()
        {
            long now = Environment.TickCount64;
            if (now - _lastReaderProbeTick < ReaderProbeIntervalMs)
                return _lastReaderProbeResult;

            _lastReaderProbeTick = now;
            try
            {
                if (EventWaitHandle.TryOpenExisting(SharedFrameConstants.ReaderAliveEventName, out var handle))
                {
                    using (handle)
                    {
                        _lastReaderProbeResult = handle.WaitOne(0);
                    }
                }
                else
                {
                    _lastReaderProbeResult = false;
                }
            }
            catch
            {
                _lastReaderProbeResult = false;
            }
            return _lastReaderProbeResult;
        }

        /// <summary>테스트 전용 — Reader 프로브 캐시 무효화 (1초 캐시 대기 제거).</summary>
        internal void ResetReaderProbeCacheForTests() => _lastReaderProbeTick = -ReaderProbeIntervalMs;

        public void WriteFrame(AcquisitionResult result)
        {
            if (_disposed || _mmf == null || _mutex == null) return;
            if (!HasReader()) return;

            var accessor = _accessor;
            if (accessor == null) return;

            bool acquired = false;
            try
            {
                acquired = _mutex.WaitOne(100);
                if (!acquired) return; // 프레임 드롭

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
                    if (_frameBuffer.Length < totalBytes)
                        _frameBuffer = new byte[totalBytes];
                    Marshal.Copy(mat.Data, _frameBuffer, 0, totalBytes);
                    accessor.WriteArray(offset, _frameBuffer, 0, totalBytes);
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
            _accessor?.Dispose();
            _mmf?.Dispose();
        }
    }
}
