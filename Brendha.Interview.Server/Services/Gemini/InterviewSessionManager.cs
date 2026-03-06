using Brendha.Interview.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Brendha.Interview.Server.Services.Gemini
{
    public class InterviewSessionManager(
        IServiceProvider serviceProvider,
        IHubContext<InterviewHub> hubContext,
        ILogger<InterviewSessionManager> logger) : IDisposable
    {

        private record SessionEntry(IServiceScope Scope, InterviewOrchestratorService Orchestrator);

        private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private readonly IHubContext<InterviewHub> _hubContext = hubContext;
        private readonly ILogger<InterviewSessionManager> _logger = logger;

        public Task CreateSessionAsync(string connectionId)
        {
            _logger.LogInformation("Creating new interview session for connectionId: {connectionId}", connectionId);

            var scope = _serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<InterviewOrchestratorService>();

            orchestrator.Initialize(connectionId);

            orchestrator.OnAudioToClientReceived += (audioBytes) => _hubContext.Clients.Client(connectionId).SendAsync("ReceiveAudio", audioBytes);
            orchestrator.OnAIQuestionChunkReceived += (chunk) => _hubContext.Clients.Client(connectionId).SendAsync("ReceiveAIQuestion", chunk);
            orchestrator.OnInterviewEnded += () => _hubContext.Clients.Client(connectionId).SendAsync("InterviewEnded");
            orchestrator.OnFeedbackGenerated += (feedback) => _hubContext.Clients.Client(connectionId).SendAsync("FeedbackGenerated", feedback);
            orchestrator.OnSessionStarted += (time) => _hubContext.Clients.Client(connectionId).SendAsync("SessionStarted", time);
            orchestrator.OnResumeReceived += (result) => _hubContext.Clients.Client(connectionId).SendAsync("ResumeReceived", result);
            orchestrator.OnFatalError += (errorData) => _hubContext.Clients.Client(connectionId).SendAsync("FatalError", errorData);
            orchestrator.OnReconnectionAttempt += (args) => _hubContext.Clients.Client(connectionId).SendAsync("ReconnectionAttempt", new { attempt = args.Attempt, maxAttempt = args.MaxAttempts });
            orchestrator.OnReconnectionSuccess += () => _hubContext.Clients.Client(connectionId).SendAsync("ReconnectionSuccess", new { message = "Reconnection successful!" });
            orchestrator.OnReconnectionFailed += (args) => _hubContext.Clients.Client(connectionId).SendAsync("ReconnectionFailed", new { attempts = args.Attempts, lastError = args.LastError, message = args.Message });

            _sessions.TryAdd(connectionId, new SessionEntry(scope, orchestrator));
            return Task.CompletedTask;
        }


        public InterviewOrchestratorService? GetSession(string connectionId)
        {
            if (_sessions.TryGetValue(connectionId, out var entry))
                return entry.Orchestrator;
            return null;
        }

        public Task RemoveSessionAsync(string connectionId)
        {
            _logger.LogInformation("Removing interview session for connectionId: {connectionId}", connectionId);
            if (_sessions.TryRemove(connectionId, out var entry))
            {
                try
                {
                    entry.Orchestrator.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing orchestrator for {connectionId}", connectionId);
                }
                finally
                {
                    entry.Scope.Dispose();
                }
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            var connectionIds = _sessions.Keys.ToList();
            foreach (var connectionId in connectionIds)
            {
                if (_sessions.TryRemove(connectionId, out var entry))
                {
                    try { entry.Orchestrator.Dispose(); } catch { }
                    try { entry.Scope.Dispose(); } catch { }
                }
            }
        }
    }
}
