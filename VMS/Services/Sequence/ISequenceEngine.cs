using VMS.PLC.Models.Sequence;

namespace VMS.Services.Sequence
{
    /// <summary>
    /// 시퀀스 노드 기반 실행 엔진 인터페이스
    /// </summary>
    public interface ISequenceEngine
    {
        /// <summary>시퀀스 실행 중 여부</summary>
        bool IsRunning { get; }

        /// <summary>현재 실행 중인 노드 ID</summary>
        string? CurrentNodeId { get; }

        /// <summary>마지막 RunAsync가 Reset 신호에 의해 중단되었는지 여부</summary>
        bool WasReset { get; }

        /// <summary>노드 실행 시작 이벤트</summary>
        event EventHandler<SequenceNodeEventArgs>? NodeExecuting;

        /// <summary>노드 실행 완료 이벤트</summary>
        event EventHandler<SequenceNodeEventArgs>? NodeCompleted;

        /// <summary>시퀀스 에러 이벤트</summary>
        event EventHandler<SequenceErrorEventArgs>? SequenceError;

        /// <summary>시퀀스 완료 이벤트</summary>
        event EventHandler? SequenceCompleted;

        /// <summary>시퀀스 1회 실행 (Start → End까지)</summary>
        Task RunAsync(SequenceConfig config, CancellationToken ct);
    }
}
