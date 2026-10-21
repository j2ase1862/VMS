using System;
using VMS.Core.Services;
using VMS.Interfaces;

namespace VMS.Services
{
    /// <summary>
    /// 검사 구간(AUTO RUN · Roller 연속 검사) 동안 쥐는 "검사 중" 신호 — 같은 PC 의 MLOps 학습 워커가
    /// 이 신호가 있으면 학습을 받지 않고, 학습 중이면 중단해 대기열로 되돌린다 (GPU 경합으로 택트가 깨지는 것 방지).
    /// 신호를 만들지 못해도 검사는 그대로 진행한다 — 경고만 남긴다.
    /// </summary>
    public static class InspectionActivity
    {
        public static IDisposable? Acquire(ISystemLogService? logService, string source)
        {
            var handle = InspectionActivitySignal.TryAcquire(out var error);
            if (handle == null)
            {
                logService?.Log(
                    $"검사 중 신호를 만들지 못했습니다 — 이 PC 에 학습 워커가 있으면 검사 중에도 학습할 수 있습니다. ({error})",
                    LogLevel.Warning, source);
            }
            return handle;
        }
    }
}
