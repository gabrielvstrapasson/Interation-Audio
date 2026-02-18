using Microsoft.Extensions.Logging.Configuration;
using System.Text;

namespace Brendha.Spike.Server.Services.Context
{
    public class InterviewContextManager(ILogger<InterviewContextManager> logger) : IDisposable
    {
        private const int MAX_RECENT_MESSAGES = 12;
        private const int CHECKPOINT_INTERVAL_SECONDS = 120;
        private const int MESSAGE_TRUNCATE_LENGTH = 150;
        private const int CHECKPOINT_VALIDITY_MINUTES = 10;

        private readonly Queue<(string role, string message)> _recentMessages = new();
        private readonly ILogger<InterviewContextManager> _logger;
        private readonly SemaphoreSlim _contextLock = new(1, 1);

        private string _lastCheckpointSummary = string.Empty;
        private DateTime _lastCheckpointTime = DateTime.MinValue;
        private Timer? _checkpointTimer;
        private bool _isDisposed = false;

        public bool HasContext => _recentMessages.Count > 0 || !string.IsNullOrEmpty(_lastCheckpointSummary);

        public void StartCheckpointTimer()
        {
            if (_isDisposed) return;

            _checkpointTimer?.Dispose();
            _checkpointTimer = new Timer(
                _ => CreateCheckpoint(),
                null,
                TimeSpan.FromSeconds(CHECKPOINT_INTERVAL_SECONDS),
                TimeSpan.FromSeconds(CHECKPOINT_INTERVAL_SECONDS)
            );
        }

        public void StopCheckpointTimer()
        {
            _checkpointTimer?.Dispose();
            _checkpointTimer = null;
            logger.LogInformation("Checkpoint timer stopped.");
        }

        public async Task AddMessageAsync(string role, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            await _contextLock.WaitAsync();
            try
            {
                _recentMessages.Enqueue((role, message));

                while (_recentMessages.Count > MAX_RECENT_MESSAGES)
                {
                    _recentMessages.Dequeue();
                }
            }
            finally
            {
                _contextLock.Release();
            }
        }

        public void CreateCheckpoint()
        {
            if (_isDisposed) return;

            try
            {
                _contextLock.Wait();
                try
                {
                    if (_recentMessages.Count == 0)
                    {
                        logger.LogInformation("No messages to create checkpoint from.");
                        return;
                    }

                    var summary = new StringBuilder();
                    summary.AppendLine("Save context");

                    foreach (var (role, message) in _recentMessages)
                    {
                        var truncatedMessage = message.Length > MESSAGE_TRUNCATE_LENGTH
                            ? message[..MESSAGE_TRUNCATE_LENGTH] + "..."
                            : message;
                        summary.AppendLine($"{role}: {truncatedMessage}");
                    }
                    _lastCheckpointSummary = summary.ToString();
                    _lastCheckpointTime = DateTime.UtcNow;

                    logger.LogInformation($"Checkpoint created with {_recentMessages.Count} messages");
                }
                finally
                {
                    _contextLock.Release();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating checkpoint");
            }
        }

        public async Task<string> BuildReconnectionContextAsync()
        {
            await _contextLock.WaitAsync();

            try
            {
                var context = new StringBuilder();
                var checkpointAge = (DateTime.UtcNow - _lastCheckpointTime).TotalMinutes;
                if (!string.IsNullOrWhiteSpace(_lastCheckpointSummary) &&
                    checkpointAge < CHECKPOINT_VALIDITY_MINUTES)
                {
                    context.AppendLine("Before context (CHECKPOINT)");
                    context.AppendLine(_lastCheckpointSummary);
                    context.AppendLine();
                }

                if (_recentMessages.Count > 0)
                {
                    context.AppendLine("Recent messages:");
                    foreach (var (role, message) in _recentMessages)
                    {
                        context.AppendLine($"{role}: {message}");
                    }
                }

                var result = context.ToString();

                if (!string.IsNullOrWhiteSpace(result))
                {
                    logger.LogInformation($"Reconnection context built: {result.Length} chars, {_recentMessages.Count} recent messages");
                }
                return result;
            }
            finally { _contextLock.Release(); }
        }
        public async Task ClearContextAsync()
        {
            await _contextLock.WaitAsync();
            try
            {
                _recentMessages.Clear();
                _lastCheckpointSummary = string.Empty;
                _lastCheckpointTime = DateTime.MinValue;
                logger.LogInformation("Context cleared");

            }
            finally
            {
                _contextLock.Release();
            }
        }




        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopCheckpointTimer();
            _contextLock?.Dispose();
        }
    }
}
