using Brendha.Spike.Server.Interfaces.Logger;

namespace Brendha.Spike.Server.Services.Context
{
    public class InterviewContextManager(
        ILogger<InterviewContextManager> logger,
        IInterviewLogger interviewLogger) : IDisposable
    {
        private const int MAX_CONTEXT_LENGTH = 4000;
        private const int MAX_RECENT_LINES = 20;

        private readonly SemaphoreSlim _contextLock = new(1, 1);
        private bool _isDisposed = false;

        public bool HasContext => !string.IsNullOrWhiteSpace(interviewLogger.GetHistory());
        public async Task<string> BuildReconnectionContextAsync()
        {
            await _contextLock.WaitAsync();

            try
            {
                var fullHistory = interviewLogger.GetHistory();

                if (string.IsNullOrWhiteSpace(fullHistory))
                {
                    logger.LogInformation("No history available for reconnection context.");
                    return string.Empty;
                }

                var lines = fullHistory.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var recentLines = lines.TakeLast(MAX_RECENT_LINES).ToArray();

                var context = string.Join('\n', recentLines);

                if (context.Length > MAX_CONTEXT_LENGTH)
                {
                    context = context[..MAX_CONTEXT_LENGTH] + "\n...(truncado)";
                }


                logger.LogInformation($"Context built: {context.Length} chars, {lines.Length} total messages");

                return context;
            }
            finally { _contextLock.Release(); }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _contextLock?.Dispose();
        }


    }
}
