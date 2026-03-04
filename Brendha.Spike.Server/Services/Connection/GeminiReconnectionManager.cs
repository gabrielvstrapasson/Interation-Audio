using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Interfaces.Connection;
using Brendha.Spike.Server.Interfaces.Logger;
using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;

namespace Brendha.Spike.Server.Services.Connection
{
    public class GeminiReconnectionManager(ILogger<IGeminiReconnectionManager> logger) : IGeminiReconnectionManager
    {
        private const int MAX_RECONNECTION_ATTEMPTS = 3;
        private const int BASE_DELAY_SECONDS = 2;

        private int _currentAttempt = 0;

        public event Func<ReconnectionAttemptArgs, Task>? OnReconnectionAttempt;
        public event Func<Task>? OnReconnectionSuccess;
        public event Func<ReconnectionFailedArgs, Task>? OnReconnectionFailed;

        public void Reset()
        {
            _currentAttempt = 0;
            logger.LogInformation("Reconnection attempts reset.");
        }

        public bool ShouldAttemptReconnection(WebSocketCloseStatus? closeStatus)
        {
            if (!closeStatus.HasValue) { return true; }

            return closeStatus.Value switch
            {
                WebSocketCloseStatus.PolicyViolation => false,
                WebSocketCloseStatus.InvalidPayloadData => false,
                WebSocketCloseStatus.InvalidMessageType => false,
                WebSocketCloseStatus.MandatoryExtension => false,
                WebSocketCloseStatus.ProtocolError => false,
                WebSocketCloseStatus.NormalClosure => false,
                _ => true
            };
        }

        public async Task<bool> TryReconnectAsync(Func<Task> connectAction, CancellationToken cancellationToken = default)
        {
            _currentAttempt = 0;

            for (int attempt = 1; attempt <= MAX_RECONNECTION_ATTEMPTS; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) { logger.LogInformation("Reconnection cancelled by user."); return false; }

                _currentAttempt = attempt;

                try
                {
                    logger.LogInformation($"Reconnection attempt {attempt}/{MAX_RECONNECTION_ATTEMPTS}");
                    OnReconnectionAttempt?.Invoke(new ReconnectionAttemptArgs(attempt, MAX_RECONNECTION_ATTEMPTS));

                    var delaySeconds = (int)Math.Pow(BASE_DELAY_SECONDS, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

                    await connectAction();
                    logger.LogInformation($"Reconnection successful on attempt {attempt}.");
                    OnReconnectionSuccess?.Invoke();

                    Reset();
                    return true;
                }
                catch (OperationCanceledException) { logger.LogWarning($"Reconnection cancelled during attempt {attempt}"); return false; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, $"Reconnection attempt {attempt} failed.");

                    if (attempt == MAX_RECONNECTION_ATTEMPTS)
                    {
                        logger.LogError("All reconnection attempts failed");

                        OnReconnectionFailed?.Invoke(new ReconnectionFailedArgs(MAX_RECONNECTION_ATTEMPTS, ex.Message, "Failed to reconnect, please restart the interview."));

                    }
                }
            }
            return false;
        }

    }
}
