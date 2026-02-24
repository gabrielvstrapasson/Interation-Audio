using System.Net.WebSockets;

namespace Brendha.Spike.Server.Interfaces.Connection
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
