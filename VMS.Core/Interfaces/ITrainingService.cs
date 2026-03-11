using System;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Models.Annotation;

namespace VMS.Core.Interfaces
{
    public interface ITrainingService
    {
        TrainingStatus Status { get; }
        bool IsTraining { get; }

        event EventHandler<string>? LogReceived;
        event EventHandler<TrainingStatus>? StatusChanged;

        Task StartTrainingAsync(TrainingConfig config, CancellationToken cancellationToken = default);
        void StopTraining();
    }
}
