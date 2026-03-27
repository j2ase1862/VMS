using System;
using System.Threading;
using System.Threading.Tasks;
using VMS.VisionSetup.Models.Annotation;

namespace VMS.VisionSetup.Interfaces
{
    /// <summary>
    /// 딥러닝 학습 프로세스 관리 서비스 인터페이스.
    /// Python 기반 학습 스크립트를 외부 프로세스로 실행하고,
    /// stdout을 파싱하여 진행률을 실시간 제공합니다.
    /// </summary>
    public interface ITrainingService
    {
        /// <summary>현재 학습 상태</summary>
        TrainingStatus Status { get; }

        /// <summary>학습 진행 중 여부</summary>
        bool IsTraining { get; }

        /// <summary>학습 로그 라인이 추가될 때 발생</summary>
        event EventHandler<string>? LogReceived;

        /// <summary>학습 상태가 변경될 때 발생</summary>
        event EventHandler<TrainingStatus>? StatusChanged;

        /// <summary>
        /// 학습 시작. Python 프로세스를 비동기로 실행합니다.
        /// </summary>
        Task StartTrainingAsync(TrainingConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// 학습 중지. 실행 중인 Python 프로세스를 종료합니다.
        /// </summary>
        void StopTraining();
    }
}
