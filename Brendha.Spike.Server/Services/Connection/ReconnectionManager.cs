using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Brendha.Spike.Server.Services.Connection
{
    public class ReconnectionManager : IDisposable
    {
        private const int MAX_RECONNECT_ATTEMPTS = 5;
        private const int INITIAL_RECONNECT_DELAY_MS = 1000;
        private const int MAX_RECONNECT_DELAY_MS = 30000;
        private const int CIRCUIT_BREAKER_THRESHOLD = 3;
        private const int CIRCUIT_BREAKER_RESET_SECONDS = 60;

        private readonly ILogger<ReconnectionManager> _logger;
        private readonly IHubContext<InterviewHub> _hubContext;
        private readonly IInterviewLogger _interviewLogger;

        private bool _isReconnecting = false;
        private bool _shouldReconnect = true;
        private int _reconnectAttempts = 0;
        private CancellationTokenSource _reconnectCts = new();
        private bool _isDisposed = false;

        private int _consecutiveFailures = 0;
        private DateTime _lastFailureTime = DateTime.MinValue;

        public bool IsReconnecting => _isReconnecting;
        public int CurrentAttempt => _reconnectAttempts;

        public ReconnectionManager(
            ILogger<ReconnectionManager> logger,
            IHubContext<InterviewHub> hubContext,
            IInterviewLogger interviewLogger)
        {
            _logger = logger;
            _hubContext = hubContext;
            _interviewLogger = interviewLogger;
        }

        public async Task<bool> TryReconnectAsync(
            Func<Task> reconnectAction,
            Action? onCheckpoint = null,
            Func<bool>? shouldContinue = null)
        {
            if (_isReconnecting || !_shouldReconnect || _isDisposed) return false;

            if (IsCircuitBreakerOpen())
            {
                _logger.LogError(
                    "Circuit breaker OPEN: {Failures} consecutive failures. Waiting {Seconds}s before retry.",
                    _consecutiveFailures, CIRCUIT_BREAKER_RESET_SECONDS);

                await _hubContext.Clients.All.SendAsync("ReconnectionBlocked", new
                {
                    reason = "Too many consecutive failures",
                    waitSeconds = CIRCUIT_BREAKER_RESET_SECONDS
                });

                return false;
            }

            _isReconnecting = true;
            _reconnectAttempts = 0;

            await NotifyReconnectionStartedAsync();

            while (_reconnectAttempts < MAX_RECONNECT_ATTEMPTS && _shouldReconnect && !_isDisposed)
            {

                if (shouldContinue != null && !shouldContinue())
                {
                    _logger.LogInformation("Reconnection cancelled by shouldContinue predicate");
                    ResetState();
                    return false;
                }

                _reconnectAttempts++;
                var delay = CalculateBackoffDelay(_reconnectAttempts);

                _logger.LogWarning(
                    "Reconnection attempt {Attempt}/{MaxAttempts} in {Delay}ms...",
                    _reconnectAttempts, MAX_RECONNECT_ATTEMPTS, delay);

                await NotifyReconnectionAttemptAsync(_reconnectAttempts, delay);

                try
                {
                    await Task.Delay(delay, _reconnectCts.Token);
                    onCheckpoint?.Invoke();
                    await reconnectAction();

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(_reconnectCts.Token);
                    cts.CancelAfter(3000); // Max 3 segundos

                    try
                    {
                        await Task.Delay(2000, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Stability check cancelled or timed out");
                    }

                    await NotifyReconnectionSuccessAsync();
                    ResetCircuitBreaker();
                    ResetState();
                    return true;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Reconnection cancelled");
                    ResetState();
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reconnection attempt {Attempt} failed", _reconnectAttempts);
                    RecordFailure();
                    

                    if (_consecutiveFailures >= CIRCUIT_BREAKER_THRESHOLD)
                    {
                        _logger.LogError("Circuit breaker triggered during reconnection attempts");
                        break;
                    }
                }
            }

            await NotifyReconnectionFailedAsync();
            ResetState();
            return false;
        }

        private bool IsCircuitBreakerOpen()
        {
            if (_consecutiveFailures < CIRCUIT_BREAKER_THRESHOLD)
                return false;

            var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime;
            if (timeSinceLastFailure.TotalSeconds >= CIRCUIT_BREAKER_RESET_SECONDS)
            {
                _logger.LogInformation("Circuit breaker reset after {Seconds}s cooldown",
                    timeSinceLastFailure.TotalSeconds);
                ResetCircuitBreaker();
                return false;
            }

            return true;
        }

        private void RecordFailure()
        {
            _consecutiveFailures++;
            _lastFailureTime = DateTime.UtcNow;
            _logger.LogWarning("Consecutive failures: {Count}", _consecutiveFailures);
        }

        private void ResetCircuitBreaker()
        {
            if (_consecutiveFailures > 0)
            {
                _logger.LogInformation("Circuit breaker CLOSED - Reset consecutive failures");
            }
            _consecutiveFailures = 0;
            _lastFailureTime = DateTime.MinValue;
        }

        public void CancelReconnection()
        {
            _shouldReconnect = false;
            _reconnectCts?.Cancel();
            _logger.LogInformation("Reconnection cancelled by user");
        }

        public void EnableReconnection()
        {
            _shouldReconnect = true;
            _reconnectCts = new CancellationTokenSource();
        }

        public void ResetState()
        {
            _isReconnecting = false;
            _reconnectAttempts = 0;
        }

        private int CalculateBackoffDelay(int attempt)
        {
            var delay = INITIAL_RECONNECT_DELAY_MS * Math.Pow(2, attempt - 1);
            return (int)Math.Min(delay, MAX_RECONNECT_DELAY_MS);
        }

        private async Task NotifyReconnectionStartedAsync()
        {
            await _hubContext.Clients.All.SendAsync("ReconnectionStarted");
            await _interviewLogger.LogAsync("System", "Connection lost. Attempting to reconnect...");
            _logger.LogWarning("Reconnection process started");
        }

        private async Task NotifyReconnectionAttemptAsync(int attempt, int delayMs)
        {
            await _hubContext.Clients.All.SendAsync("ReconnectionAttempt", new
            {
                attempt,
                maxAttempts = MAX_RECONNECT_ATTEMPTS,
                delayMs
            });
        }

        private async Task NotifyReconnectionSuccessAsync()
        {
            _logger.LogInformation("Reconnection successful!");
            await _hubContext.Clients.All.SendAsync("ReconnectionSuccess");
            await _interviewLogger.LogAsync("System", "Reconnected successfully. Continuing interview...");
        }

        private async Task NotifyReconnectionFailedAsync()
        {
            _logger.LogError("Max reconnection attempts reached. Connection failed.");
            await _hubContext.Clients.All.SendAsync("ReconnectionFailed", new
            {
                message = "Unable to reconnect to Gemini API"
            });
            await _interviewLogger.LogAsync("System", "Failed to reconnect. Interview interrupted.");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            CancelReconnection();
            _reconnectCts?.Dispose();
        }
    }
}