using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure.Interfaces;
using Brendha.Spike.Server.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;

namespace Brendha.Spike.Server.Services.GeminiConnection
{
    public class GeminiReconnectionManager(
        ILogger<IGeminiReconnectionManager> logger,
        IInterviewLogger interviewLogger,
        IHubContext<InterviewHub> hubContext) : IGeminiReconnectionManager
    {
        private const int MAX_RECONNECTION_ATTEMPTS = 3;
        private const int BASE_DELAY_SECONDS = 2;

        private int _currentAttempt = 0;

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
                    await interviewLogger.LogAsync("System", $"Trying to reconnect ({attempt}/{MAX_RECONNECTION_ATTEMPTS})");

                    await hubContext.Clients.All.SendAsync("ReconnectionAttempt", new
                    {
                        attempt,
                        maxAttempt = MAX_RECONNECTION_ATTEMPTS
                    }, cancellationToken);
                    var delaySeconds = (int)Math.Pow(BASE_DELAY_SECONDS, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

                    await connectAction();
                    logger.LogInformation($"Reconnection successful on attempt {attempt}.");
                    await interviewLogger.LogAsync("System", "Reconnection successful.");

                    await hubContext.Clients.All.SendAsync("ReconnectionSuccess", new
                    {
                        attempt,
                        message = "Reconnection successful!"
                    }, cancellationToken);

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
                        await interviewLogger.LogAsync("System", "Fail to reconnect after multiple attempts");

                        await hubContext.Clients.All.SendAsync("ReconnectionFailed", new
                        {
                            attempts = MAX_RECONNECTION_ATTEMPTS,
                            lastError = ex.Message,
                            message = "Failed to reconnect, please restart the interview."
                        }, cancellationToken);
                    }
                }
            }
            return false;
        }

    }
}
