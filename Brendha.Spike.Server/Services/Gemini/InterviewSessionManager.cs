using Brendha.Spike.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Brendha.Spike.Server.Services.Gemini
{
    public class InterviewSessionManager(
        IServiceProvider serviceProvider,
        IHubContext<InterviewHub> hubContext,
        ILogger<InterviewSessionManager> logger) : IDisposable
    {

        private readonly ConcurrentDictionary<string, InterviewOrchestratorService> _sessions = new();
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private readonly IHubContext<InterviewHub> _hubContext = hubContext;
        private readonly ILogger<InterviewSessionManager> _logger = logger;

        public Task CreateSessionAsync(string connectionId)
        {
            _logger.LogInformation("Creating new interview session for connectionId: {connectionId}", connectionId);
            var orchestrator = _serviceProvider.GetRequiredService<InterviewOrchestratorService>();

            if (_sessions.TryAdd(connectionId, orchestrator))
            {
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
            }

            return Task.CompletedTask;
        }


        public InterviewOrchestratorService? GetSession(string connectionId)
        {
            _sessions.TryGetValue(connectionId, out var session);
            return session;
        }

        public Task RemoveSessionAsync(string connectionId)
        {
            _logger.LogInformation("Removing interview session for connectionId: {connectionId}", connectionId);
            if (_sessions.TryRemove(connectionId, out var orchestrator))
            {
                orchestrator.Dispose();
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            foreach (var connectionId in _sessions.Keys)
            {
                RemoveSessionAsync(connectionId).Wait();
            }
        }
    }
}
