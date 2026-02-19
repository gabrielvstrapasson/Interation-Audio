using System.Net.WebSockets;

namespace Brendha.Spike.Server.Services.Interfaces
{
    public interface IGeminiReconnectionManager
    {
        Task<bool> TryReconnectAsync(
            Func<Task> connectAction,
            CancellationToken cancellationToken = default);

        void Reset();
        bool ShouldAttemptReconnection(WebSocketCloseStatus? closeStatus);
    }
}
