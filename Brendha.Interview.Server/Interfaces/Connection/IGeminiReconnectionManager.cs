using System.Net.WebSockets;

namespace Brendha.Interview.Server.Interfaces.Connection
{
    public record ReconnectionAttemptArgs(int Attempt, int MaxAttempts);
    public record ReconnectionFailedArgs(int Attempts, string LastError, string Message);
    public interface IGeminiReconnectionManager
    {
        event Func<ReconnectionAttemptArgs, Task>? OnReconnectionAttempt;  
        event Func<Task>? OnReconnectionSuccess;
        event Func<ReconnectionFailedArgs, Task>? OnReconnectionFailed;
        void Reset();
        bool ShouldAttemptReconnection(WebSocketCloseStatus? closeStatus);
        Task<bool> TryReconnectAsync(Func<Task> connectAction, CancellationToken cancellationToken = default);

    }
}
