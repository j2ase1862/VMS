using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// VisionSetup → VMS Grab 요청 채널 (MMF + 이벤트 2개).
    ///
    /// 프레임 채널(SharedFrameWriter/Reader)은 VMS → VisionSetup 단방향이라, VisionSetup 은
    /// VMS 창으로 가서 Grab 을 누르고 돌아와야 했다. 이 채널이 그 왕복을 없앤다.
    ///
    /// 요청/응답을 쌍으로 두는 이유: 운전(AUTO RUN)·라이브 중에는 거절해야 하는데,
    /// 단방향 신호만 보내면 요청 측이 사유를 모른 채 타임아웃까지 기다리게 된다.
    ///
    /// 레이아웃 (little-endian):
    ///   0  RequestId      (long,  8)
    ///   8  Status         (int,   4)   GrabRequestStatus
    ///   12 CameraIdLength (int,   4)
    ///   16 MessageLength  (int,   4)
    ///   20 FrameCounter   (long,  8)
    ///   28 (예약)         (int,   4)
    ///   32 CameraId UTF8 → MessageLength UTF8
    /// </summary>
    public sealed class GrabRequestChannel : IDisposable
    {
        private const int OffsetRequestId = 0;
        private const int OffsetStatus = 8;
        private const int OffsetCameraIdLength = 12;
        private const int OffsetMessageLength = 16;
        private const int OffsetFrameCounter = 20;
        private const int BodyOffset = 32;
        private const int Capacity = 4096;
        private const int MaxTextBytes = 1024;

        private MemoryMappedFile? _mmf;
        private Mutex? _mutex;
        private EventWaitHandle? _requestReady;
        private EventWaitHandle? _responseReady;
        private bool _disposed;
        private long _nextRequestId;

        /// <summary>
        /// 수신 측(VMS)에서 채널을 생성한다. VMS 가 없으면 요청 측의 TryConnect 가 실패해
        /// "VMS 미실행"으로 구분된다.
        /// </summary>
        public void InitializeAsResponder()
        {
            _mmf = MemoryMappedFile.CreateOrOpen(SharedFrameConstants.GrabRequestMmfName, Capacity);
            _mutex = new Mutex(false, SharedFrameConstants.GrabRequestMutexName);
            _requestReady = new EventWaitHandle(
                false, EventResetMode.AutoReset, SharedFrameConstants.GrabRequestReadyEventName);
            _responseReady = new EventWaitHandle(
                false, EventResetMode.AutoReset, SharedFrameConstants.GrabResponseReadyEventName);
        }

        /// <summary>요청 측(VisionSetup)에서 기존 채널에 연결. VMS 미실행이면 false.</summary>
        public bool TryConnectAsRequester()
        {
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(SharedFrameConstants.GrabRequestMmfName);
                _mutex = Mutex.OpenExisting(SharedFrameConstants.GrabRequestMutexName);
                _requestReady = EventWaitHandle.OpenExisting(SharedFrameConstants.GrabRequestReadyEventName);
                _responseReady = EventWaitHandle.OpenExisting(SharedFrameConstants.GrabResponseReadyEventName);
                return true;
            }
            catch
            {
                Dispose();
                return false;
            }
        }

        // ── 요청 측 ──

        /// <summary>
        /// Grab 을 요청하고 응답을 기다린다. 응답이 timeoutMs 안에 오지 않으면 null.
        /// 응답의 RequestId 가 보낸 것과 다르면 이전 요청의 잔여 응답이므로 계속 기다린다.
        /// </summary>
        public GrabResponse? RequestGrab(string cameraId, int timeoutMs = 10000)
        {
            if (_disposed || _mmf == null || _mutex == null
                || _requestReady == null || _responseReady == null)
                return null;

            long requestId = Interlocked.Increment(ref _nextRequestId) * 1000
                + (Environment.TickCount64 & 0x3FF);

            bool acquired = false;
            try
            {
                acquired = _mutex.WaitOne(1000);
                if (!acquired) return null;

                using var accessor = _mmf.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);
                var idBytes = Encode(cameraId);

                accessor.Write(OffsetRequestId, requestId);
                accessor.Write(OffsetStatus, (int)GrabRequestStatus.Pending);
                accessor.Write(OffsetCameraIdLength, idBytes.Length);
                accessor.Write(OffsetMessageLength, 0);
                accessor.Write(OffsetFrameCounter, 0L);
                if (idBytes.Length > 0)
                    accessor.WriteArray(BodyOffset, idBytes, 0, idBytes.Length);
            }
            finally
            {
                if (acquired) _mutex.ReleaseMutex();
            }

            // 응답 대기 전에 이전 신호를 비운다 — 남아 있으면 즉시 깨어나 Pending 을 읽는다
            _responseReady.Reset();
            _requestReady.Set();

            long deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                int remaining = (int)Math.Max(1, deadline - Environment.TickCount64);
                if (!_responseReady.WaitOne(remaining))
                    return null;

                var response = ReadResponse();
                if (response != null && response.RequestId == requestId)
                    return response;
                // 다른 요청의 응답 — 계속 대기
            }
            return null;
        }

        private GrabResponse? ReadResponse()
        {
            if (_mmf == null || _mutex == null) return null;

            bool acquired = false;
            try
            {
                acquired = _mutex.WaitOne(1000);
                if (!acquired) return null;

                using var accessor = _mmf.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.Read);
                int status = accessor.ReadInt32(OffsetStatus);
                if (status == (int)GrabRequestStatus.Pending) return null;

                int idLen = ClampTextLength(accessor.ReadInt32(OffsetCameraIdLength));
                int msgLen = ClampTextLength(accessor.ReadInt32(OffsetMessageLength));

                return new GrabResponse
                {
                    RequestId = accessor.ReadInt64(OffsetRequestId),
                    Status = (GrabRequestStatus)status,
                    FrameCounter = accessor.ReadInt64(OffsetFrameCounter),
                    CameraId = ReadText(accessor, BodyOffset, idLen),
                    Message = ReadText(accessor, BodyOffset + idLen, msgLen)
                };
            }
            finally
            {
                if (acquired) _mutex.ReleaseMutex();
            }
        }

        // ── 수신 측 ──

        /// <summary>요청 도착 대기. 도착하면 요청 내용을 반환, 타임아웃이면 null.</summary>
        public GrabRequest? WaitForRequest(int timeoutMs)
        {
            if (_disposed || _mmf == null || _mutex == null || _requestReady == null) return null;
            if (!_requestReady.WaitOne(timeoutMs)) return null;

            bool acquired = false;
            try
            {
                acquired = _mutex.WaitOne(1000);
                if (!acquired) return null;

                using var accessor = _mmf.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.Read);
                int idLen = ClampTextLength(accessor.ReadInt32(OffsetCameraIdLength));
                return new GrabRequest
                {
                    RequestId = accessor.ReadInt64(OffsetRequestId),
                    CameraId = ReadText(accessor, BodyOffset, idLen)
                };
            }
            finally
            {
                if (acquired) _mutex.ReleaseMutex();
            }
        }

        /// <summary>처리 결과를 기록하고 요청 측을 깨운다.</summary>
        public void Respond(GrabResponse response)
        {
            if (_disposed || _mmf == null || _mutex == null || _responseReady == null) return;

            bool acquired = false;
            try
            {
                acquired = _mutex.WaitOne(1000);
                if (!acquired) return;

                using var accessor = _mmf.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);
                var idBytes = Encode(response.CameraId);
                var msgBytes = Encode(response.Message);

                accessor.Write(OffsetRequestId, response.RequestId);
                accessor.Write(OffsetStatus, (int)response.Status);
                accessor.Write(OffsetCameraIdLength, idBytes.Length);
                accessor.Write(OffsetMessageLength, msgBytes.Length);
                accessor.Write(OffsetFrameCounter, response.FrameCounter);
                if (idBytes.Length > 0)
                    accessor.WriteArray(BodyOffset, idBytes, 0, idBytes.Length);
                if (msgBytes.Length > 0)
                    accessor.WriteArray(BodyOffset + idBytes.Length, msgBytes, 0, msgBytes.Length);
            }
            finally
            {
                if (acquired) _mutex.ReleaseMutex();
            }

            _responseReady.Set();
        }

        // ── 공통 ──

        private static byte[] Encode(string? text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length <= MaxTextBytes) return bytes;
            // 잘라내면 UTF-8 시퀀스 중간이 깨질 수 있으므로 통째로 버린다
            return Array.Empty<byte>();
        }

        private static int ClampTextLength(int length)
            => length < 0 || length > MaxTextBytes ? 0 : length;

        private static string ReadText(MemoryMappedViewAccessor accessor, long offset, int length)
        {
            if (length <= 0) return string.Empty;
            var buffer = new byte[length];
            accessor.ReadArray(offset, buffer, 0, length);
            return Encoding.UTF8.GetString(buffer);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _requestReady?.Dispose(); } catch { }
            try { _responseReady?.Dispose(); } catch { }
            try { _mutex?.Dispose(); } catch { }
            try { _mmf?.Dispose(); } catch { }
            _requestReady = null;
            _responseReady = null;
            _mutex = null;
            _mmf = null;
        }
    }
}
