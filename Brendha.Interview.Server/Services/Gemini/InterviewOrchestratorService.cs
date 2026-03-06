using Brendha.Interview.Core.Models.DTOs.Agents;
using Brendha.Interview.Core.Models.DTOs.Gemini;
using Brendha.Interview.Core.Models.State;
using Brendha.Interview.Server.Hubs;
using Brendha.Interview.Server.Interfaces.Agents;
using Brendha.Interview.Server.Interfaces.Connection;
using Brendha.Interview.Server.Interfaces.Context;
using Brendha.Interview.Server.Interfaces.Helpers;
using Brendha.Interview.Server.Interfaces.Logger;
using Brendha.Interview.Server.Interfaces.Processing;
using Brendha.Interview.Server.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using System.Text.Json;

namespace Brendha.Interview.Server.Services.Gemini
{
    public class InterviewOrchestratorService(
        ILogger<InterviewOrchestratorService> logger,
        IHubContext<InterviewHub> hubContext,
        IInterviewLoggerFactory interviewLoggerFactory,
        IInterviewContextManager contextManager,
        IGeminiConnectionManager connectionManager,
        IGeminiReconnectionManager reconnectionManager,
        IGeminiMessageProcessor messageProcessor,
        IGeminiSetupConfigBuilder setupConfigBuilder,
        IGeminiFeedbackAgent feedbackAgent,
        IJobService jobService
        ) : IDisposable
    {
        private readonly InterviewSessionState _sessionState = new();
        private IInterviewFileLogger _interviewFileLogger = null!;
        private bool _isDisposed = false;
        private bool _handlersAttached = false;

        private readonly ILogger<InterviewOrchestratorService> _logger = logger;
        private readonly IHubContext<InterviewHub> _hubContext = hubContext;
        private readonly IInterviewContextManager _contextManager = contextManager;
        private readonly IGeminiConnectionManager _connectionManager = connectionManager;
        private readonly IJobService _jobService = jobService;
        private readonly IGeminiReconnectionManager _reconnectionManager = reconnectionManager;
        private readonly IGeminiMessageProcessor _messageProcessor = messageProcessor;
        private readonly IGeminiSetupConfigBuilder _setupConfigBuilder = setupConfigBuilder;
        private readonly IGeminiFeedbackAgent _feedbackAgent = feedbackAgent;

        public event Func<byte[], Task>? OnAudioToClientReceived;
        public event Func<string, Task>? OnAIQuestionChunkReceived;
        public event Func<Task>? OnInterviewEnded;
        public event Func<FeedbackResultDto, Task>? OnFeedbackGenerated;
        public event Func<double, Task>? OnSessionStarted;
        public event Func<object, Task>? OnResumeReceived;
        public event Func<object, Task>? OnFatalError;
        public event Func<ReconnectionAttemptArgs, Task>? OnReconnectionAttempt;
        public event Func<Task>? OnReconnectionSuccess;
        public event Func<ReconnectionFailedArgs, Task>? OnReconnectionFailed;

        public bool IsConnected => connectionManager.IsConnected;
        public bool HasResume => !string.IsNullOrWhiteSpace(_sessionState.PendingResumeText) || !string.IsNullOrWhiteSpace(_sessionState.CurrentResumeText);

        public void Initialize(string connectionId)
        {
            _interviewFileLogger = interviewLoggerFactory.Create(connectionId);
            _logger.LogInformation("InterviewOrchestratorService initialized for connection {connectionId}", connectionId);
        }
        public async Task ConnectAsync()
        {
            AttachHandlersOnce();

            if (_isDisposed) throw new ObjectDisposedException(nameof(InterviewOrchestratorService));

            try
            {
                if (!string.IsNullOrWhiteSpace(_sessionState.PendingResumeText))
                {
                    _sessionState.CurrentResumeText = _sessionState.PendingResumeText;
                    _logger.LogInformation("Resume loaded into interview context");
                }

                await _connectionManager.ConnectAsync();
                await InitializeSessionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to connect and initialize session: {ex.Message}");
                await DisconnectAsync();
                throw;
            }
        }

        public async Task SetResumeAsync(string resumeJson)
        {
            if (string.IsNullOrWhiteSpace(resumeJson))
            {
                _logger.LogWarning("Received empty resume JSON.");
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(resumeJson);
                if (doc.RootElement.TryGetProperty("resumeText", out var el) ||
                    doc.RootElement.TryGetProperty("resume_text", out el))
                {
                    _sessionState.PendingResumeText = el.GetString()!;
                    _logger.LogInformation("Resume set successfully. Ready to start interview");
                    OnResumeReceived?.Invoke(new { success = true });
                }
                else
                {
                    _logger.LogWarning("Property 'resumeText' not found in JSON");
                    OnResumeReceived?.Invoke(new { success = false, error = "Invalid format" });
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Invalid JSON format for resume.");
                OnResumeReceived?.Invoke(new { success = false, error = "Invalid format" });
            }
        }
        public async Task LogSystemMessage(string text) => await _interviewFileLogger.LogAsync("System:", text);
        public async Task SendAudioChunkAsync(byte[] audioData)
        {
            if (!_connectionManager.IsConnected) return;

            var payload = new RealtimeInputRequest(new RealtimeInput(new[]
            {
                new MediaChunk("audio/pcm", Convert.ToBase64String(audioData))
            }));

            if (!_sessionState.AiIsSpeaking && !_sessionState.IsWaitingForFirstResponse)
            {
                _sessionState.LatencyWatch.Restart();
                _sessionState.IsWaitingForFirstResponse = true;
            }
            await _connectionManager.SendJsonAsync(JsonSerializer.Serialize(payload));
        }
        public double GetTimeRemaining()
        {
            var remaining = (_sessionState.SessionEndTime - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? remaining : 0;
        }
        public async Task<FeedbackResultDto> DisconnectAsync()
        {
            try
            {
                _logger.LogInformation("Closing Gemini Connection");

                await _connectionManager.DisconnectAsync();
                await _interviewFileLogger.LogAsync("System", "Interview ended");

                var feedback = await _feedbackAgent.GenerateFeedbackAsync(_interviewFileLogger.LogFilePath, _sessionState.CurrentJob);

                OnFeedbackGenerated?.Invoke(feedback);
                OnInterviewEnded?.Invoke();
                _logger.LogInformation("Interview ended successfully and feedback generated");

                _interviewFileLogger.FinalizeAndReset();
                return feedback;
            }

            catch (Exception ex)
            {
                _logger.LogError($"Error closing Gemini connection: {ex.Message}");
                return null;
            }
        }
        private async Task InitializeSessionAsync()
        {
            _sessionState.SessionEndTime = DateTime.UtcNow.AddMinutes(10);
            OnSessionStarted.Invoke(GetTimeRemaining());

            _sessionState.CurrentJob = await _jobService.GetCurrentJobAsync();
            var history = _interviewFileLogger.GetHistory();
            var reconnectionContext = await contextManager.BuildReconnectionContextAsync(history);

            if (!string.IsNullOrWhiteSpace(reconnectionContext))
            {
                _logger.LogInformation($"Reconnection with context recovery from history");
                await _interviewFileLogger.LogAsync("System", "Reconnection with context recovery from history");
            }
            else { _logger.LogInformation("Starting fresh interview"); }

            var config = setupConfigBuilder.Build(_sessionState.CurrentJob, _sessionState.CurrentResumeText, reconnectionContext);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var configJson = JsonSerializer.Serialize(config, options);
            await _connectionManager.SendJsonAsync(configJson);

            logger.LogInformation("New sesion started");
        }
        #region Handlers
        private void AttachHandlersOnce()
        {
            if (_handlersAttached) return;
            _handlersAttached = true;

            _connectionManager.OnMessageReceived += HandleMessageReceived;
            _connectionManager.OnConnectionClosed += HandleConnectionClosed;

            _reconnectionManager.OnReconnectionAttempt += HandleReconnectionAttempt;
            _reconnectionManager.OnReconnectionSuccess += HandleReconnetionSuccess;
            _reconnectionManager.OnReconnectionFailed += HandleReconnectionFailed;
            _messageProcessor.OnAudioReceived += HandleAudioReceived;
            _messageProcessor.OnAITextReceived += HandleAITextReceived;
            _messageProcessor.OnAITextChunkReceived += HandleAITextChunkReceived;
            _messageProcessor.OnUserTextReceived += HandleUserTextReceived;
            _messageProcessor.OnTurnComplete += HandleTurnComplete;
            _messageProcessor.OnToolCallReceived += HandleToolCallReceived;
        }

        private Task HandleMessageReceived(string json) => _messageProcessor.ProcessAsync(json);

        private async Task HandleConnectionClosed(WebSocketCloseStatus? status, string? description)
        {
            if (status.HasValue && status != WebSocketCloseStatus.NormalClosure)
            {
                _logger.LogError("Non-recoverable error: {Status}. Notifying client.", status);
                var errorData = new { status = status.ToString(), description, message = "A non-recoverable error occurred. Please restart the interview." };
                OnFatalError?.Invoke(errorData);
                await _interviewFileLogger.LogAsync("System", $"Fatal error: {status} - {description}");
            }
        }

        private async Task HandleAudioReceived(byte[] bytes)
        {
            try
            {
                if (_sessionState.IsWaitingForFirstResponse)
                {
                    _sessionState.LatencyWatch.Stop();
                    _sessionState.AiIsSpeaking = true;
                    _logger.LogInformation("Ping: {ms}ms", _sessionState.LatencyWatch.ElapsedMilliseconds);
                    _sessionState.IsWaitingForFirstResponse = false;
                }
                OnAudioToClientReceived?.Invoke(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling audio from message processor");
            }
        }

        private async Task HandleAITextReceived(string text)
        {
            await _interviewFileLogger.LogAsync("Brendha (AI): ", text);
            _logger.LogInformation("Brendha(IA): {text}", text);
            if (text.Contains("[FINISH_INTERVIEW]"))
            {
                _logger.LogInformation("Detected [FINISH_INTERVIEW] tag in AI response, ending session");
                await Task.Delay(2000);
                await DisconnectAsync();
            }
        }

        private Task HandleAITextChunkReceived(string chunk)
        {
            var cleanChunk = chunk.Replace("[FINISH_INTERVIEW]", "");
            if (!string.IsNullOrWhiteSpace(cleanChunk))
            {
                OnAIQuestionChunkReceived?.Invoke(chunk);
            }
            return Task.CompletedTask;
        }

        private async Task HandleUserTextReceived(string text)
        {
            await _interviewFileLogger.LogAsync("Candidate: ", text);
            _logger.LogInformation("Candidate: {text}", text);
        }

        private Task HandleTurnComplete()
        {
            _sessionState.AiIsSpeaking = false;
            return Task.CompletedTask;
        }

        private async Task HandleToolCallReceived(JsonElement toolCallJson)
        {
            try
            {
                if (toolCallJson.TryGetProperty("functionCalls", out var calls) || toolCallJson.TryGetProperty("function_calls", out calls))
                {
                    foreach (var call in calls.EnumerateArray())
                    {
                        if (call.GetProperty("name").GetString() == "finish_interview")
                        {
                            _logger.LogInformation("Received finish_interview tool call from Gemini, ending session");
                            await Task.Delay(500);
                            await DisconnectAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error to process Tool Call finish_interview");
            }
        }
        
        private async Task HandleReconnectionAttempt (ReconnectionAttemptArgs args) 
        {
            await _interviewFileLogger.LogAsync("System", $"Reconnection attempt {args.Attempt}/{args.MaxAttempts}");
            OnReconnectionAttempt?.Invoke(args);
        }
        private async Task HandleReconnetionSuccess()
        {
            await _interviewFileLogger.LogAsync("System", "Reconnection successful");
            OnReconnectionSuccess?.Invoke();
        }
        private async Task HandleReconnectionFailed(ReconnectionFailedArgs args)
        {
            await _interviewFileLogger.LogAsync("System", $"Fail to reconnect after multiple attempts");
            OnReconnectionFailed?.Invoke(args);
        }

        #endregion
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_handlersAttached)
            {
                _connectionManager.OnMessageReceived -= HandleMessageReceived;
                _connectionManager.OnConnectionClosed -= HandleConnectionClosed;
                _reconnectionManager.OnReconnectionAttempt -= HandleReconnectionAttempt;
                _reconnectionManager.OnReconnectionSuccess -= HandleReconnetionSuccess;
                _reconnectionManager.OnReconnectionFailed -= HandleReconnectionFailed;
                _messageProcessor.OnAudioReceived -= HandleAudioReceived;
                _messageProcessor.OnAITextReceived -= HandleAITextReceived;
                _messageProcessor.OnAITextChunkReceived -= HandleAITextChunkReceived;
                _messageProcessor.OnUserTextReceived -= HandleUserTextReceived;
                _messageProcessor.OnTurnComplete -= HandleTurnComplete;
                _messageProcessor.OnToolCallReceived -= HandleToolCallReceived;
            }

            _contextManager?.Dispose();
            _connectionManager?.Dispose();
        }

    }
}
