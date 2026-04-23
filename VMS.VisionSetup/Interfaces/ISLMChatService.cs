using System;
using System.Threading;
using System.Threading.Tasks;

namespace VMS.VisionSetup.Interfaces
{
    public interface ISLMChatService : IDisposable
    {
        bool IsModelLoaded { get; }
        Task LoadModelAsync(string modelName);
        Task<string> SendMessageAsync(string message, Action<string>? onToken = null, CancellationToken cancellationToken = default);
        void ResetSession();
        void SetMode(bool isOperatorMode);
        void SetCanvasContext(string? context);
    }
}
