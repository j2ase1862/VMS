using System;
using System.Threading;
using System.Threading.Tasks;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// VisionSetup 의 Grab 요청을 받아 VMS 카메라로 Grab 을 수행하는 수신 루프.
    ///
    /// 이걸 두는 이유: VisionSetup 은 VMS 가 떠 있으면 카메라를 직접 잡을 수 없다
    /// (소유권은 VMS). 그래서 지금까지는 VMS 창으로 가서 Grab → VisionSetup 으로 돌아와
    /// 수신하는 왕복이 필요했다. 이 루프가 그 왕복을 없앤다.
    ///
    /// 운전(AUTO RUN)·라이브 중에는 카메라를 뺏으면 안 되므로 거절하고 사유를 돌려준다.
    /// </summary>
    public sealed class GrabRequestListener : IDisposable
    {
        private readonly GrabRequestChannel _channel = new();
        private readonly Func<GrabRequestContext> _contextProvider;
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private bool _disposed;

        /// <summary>
        /// 요청 처리에 필요한 VMS 상태 스냅샷 + 실행 콜백.
        /// ViewModel 을 직접 참조하지 않고 델리게이트로 받아 테스트가 가능하다.
        /// </summary>
        public sealed class GrabRequestContext
        {
            public bool IsAutoRunning { get; init; }
            public bool IsLiveMode { get; init; }

            /// <summary>카메라 Id → Grab 수행. 성공 시 true. 없는 카메라면 null 반환.</summary>
            public Func<string, Task<bool>>? GrabAsync { get; init; }

            /// <summary>요청에 카메라가 지정되지 않았을 때 쓸 기본 카메라 Id (없으면 빈 문자열).</summary>
            public string DefaultCameraId { get; init; } = string.Empty;

            /// <summary>해당 Id 의 카메라가 존재하는지.</summary>
            public Func<string, bool>? CameraExists { get; init; }

            /// <summary>공유 메모리에 마지막으로 기록된 프레임 번호 (응답에 실어 보냄).</summary>
            public Func<long>? CurrentFrameCounter { get; init; }
        }

        public GrabRequestListener(Func<GrabRequestContext> contextProvider)
        {
            _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
        }

        public void Start()
        {
            if (_disposed || _loop != null) return;

            _channel.InitializeAsResponder();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            _loop = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    // 짧은 타임아웃으로 폴링 — 종료 시 즉시 빠져나오기 위함
                    var request = _channel.WaitForRequest(500);
                    if (request == null) continue;

                    GrabResponse response;
                    try
                    {
                        response = await HandleAsync(request).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        response = new GrabResponse
                        {
                            RequestId = request.RequestId,
                            Status = GrabRequestStatus.GrabFailed,
                            CameraId = request.CameraId,
                            Message = ex.Message
                        };
                    }

                    _channel.Respond(response);
                }
            }, ct);
        }

        internal async Task<GrabResponse> HandleAsync(GrabRequest request)
        {
            var ctx = _contextProvider();

            GrabResponse Reject(GrabRequestStatus status, string message) => new()
            {
                RequestId = request.RequestId,
                Status = status,
                CameraId = request.CameraId,
                Message = message
            };

            // 운전/라이브 중에는 카메라를 뺏지 않는다 — 검사 사이클이 깨진다
            if (ctx.IsAutoRunning)
                return Reject(GrabRequestStatus.RejectedAutoRun, "VMS가 운전(AUTO RUN) 중입니다.");
            if (ctx.IsLiveMode)
                return Reject(GrabRequestStatus.RejectedLive, "VMS가 라이브 구동 중입니다.");

            string cameraId = string.IsNullOrEmpty(request.CameraId)
                ? ctx.DefaultCameraId
                : request.CameraId;

            if (string.IsNullOrEmpty(cameraId))
                return Reject(GrabRequestStatus.CameraNotFound, "VMS에 사용할 카메라가 없습니다.");

            if (ctx.CameraExists != null && !ctx.CameraExists(cameraId))
                return Reject(GrabRequestStatus.CameraNotFound,
                    $"VMS에 해당 카메라가 없습니다 (Id: {cameraId}).");

            if (ctx.GrabAsync == null)
                return Reject(GrabRequestStatus.GrabFailed, "VMS Grab 핸들러가 준비되지 않았습니다.");

            bool ok = await ctx.GrabAsync(cameraId).ConfigureAwait(false);

            return new GrabResponse
            {
                RequestId = request.RequestId,
                Status = ok ? GrabRequestStatus.Success : GrabRequestStatus.GrabFailed,
                CameraId = cameraId,
                FrameCounter = ctx.CurrentFrameCounter?.Invoke() ?? 0,
                Message = ok ? string.Empty : "VMS에서 Grab에 실패했습니다."
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cts?.Cancel(); } catch { }
            try { _loop?.Wait(2000); } catch { /* 종료 경로 — 대기 실패해도 계속 */ }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            _loop = null;
            _channel.Dispose();
        }
    }
}
