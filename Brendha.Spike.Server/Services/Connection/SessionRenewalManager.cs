namespace Brendha.Spike.Server.Services.Connection
{
    public class SessionRenewalManager(ILogger<SessionRenewalManager> logger) : IDisposable
    {
        private const int SESSION_DURATION_MINUTES = 10;
        private const int RENEWAL_BUFFER_SECONDS = 30;

        private Timer? _renewalTimer;
        private bool _isDisposed = false;
        private DateTime _sessionStartTime;
        private int _renewalCount = 0;
        private bool _isRenewing = false;

        public int RenewalCount => _renewalCount;
        public DateTime SessionStartTime => _sessionStartTime;
        public TimeSpan TimeUntilRenewal => CalculateTimeUntilRenewal();
        public bool IsRenewing => _isRenewing;


        public void StartRenewalTimer(Func<Task> renewalAction)
        {
            if (_isDisposed) return;

            if (_isRenewing)
            {
                logger.LogDebug("Skipping timer creation - renewal in progress");
                return;
            }

            _sessionStartTime = DateTime.UtcNow;
            _renewalTimer?.Dispose();

            var renewalDelay = CalculateRenewalDelay();

            _renewalTimer = new Timer(
                async _ => await ExecuteRenewalAsync(renewalAction),
                null,
                renewalDelay,
                Timeout.InfiniteTimeSpan
            );

            logger.LogInformation(
                "Session renewal timer started. Will renew in {Minutes} minutes {Seconds} seconds",
                renewalDelay.Minutes, renewalDelay.Seconds);
        }

        public void StopRenewalTimer()
        {
            _renewalTimer?.Dispose();
            _renewalTimer = null;
            logger.LogInformation("Session renewal timer stopped");
        }
        public void Reset()
        {
            _renewalCount = 0;
            _sessionStartTime = DateTime.MinValue;
            _isRenewing = false;
            StopRenewalTimer();
        }

        private TimeSpan CalculateRenewalDelay()
        {
            var totalSeconds = (SESSION_DURATION_MINUTES * 60) - RENEWAL_BUFFER_SECONDS;
            return TimeSpan.FromSeconds(totalSeconds);
        }

        private TimeSpan CalculateTimeUntilRenewal()
        {
            if (_sessionStartTime == DateTime.MinValue)
                return TimeSpan.Zero;

            var elapsed = DateTime.UtcNow - _sessionStartTime;
            var renewalTime = TimeSpan.FromMinutes(SESSION_DURATION_MINUTES) - TimeSpan.FromSeconds(RENEWAL_BUFFER_SECONDS);
            var remaining = renewalTime - elapsed;

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        private async Task ExecuteRenewalAsync(Func<Task> renewalAction)
        {
            if (_isRenewing)
            {
                logger.LogWarning("Renewal already in progress, skipping");
                return;
            }

            _isRenewing = true;

            try
            {
                _renewalCount++;
                logger.LogWarning(
                    "Session approaching expiration. Initiating renewal #{RenewalCount}...",
                    _renewalCount);

                await renewalAction();

                logger.LogInformation("Session renewed successfully (renewal #{RenewalCount})", _renewalCount);

                await Task.Delay(1000);

                StartRenewalTimer(renewalAction);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to renew session automatically");
            }
            finally { _isRenewing = false; }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopRenewalTimer();
        }
    }
}
