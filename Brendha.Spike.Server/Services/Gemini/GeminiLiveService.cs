using Brendha.Spike.Core.Models.DTOs.Gemini;
using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Interfaces.Connection;
using Brendha.Spike.Server.Interfaces.Helpers;
using Brendha.Spike.Server.Interfaces.Logger;
using Brendha.Spike.Server.Interfaces.Processing;
using Brendha.Spike.Server.Interfaces.Transport;
using Brendha.Spike.Server.Services.Context;
using Brendha.Spike.Server.Services.Data;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Brendha.Spike.Server.Services.Gemini
{
    public class GeminiLiveService(
        string apiKey,
        ILogger<GeminiLiveService> logger,
        IHubContext<InterviewHub> hubContext,
        IInterviewLogger interviewLogger,
        InterviewContextManager contextManager,
        IGeminiReconnectionManager reconnectionManager,
        IWebSocketClient webSocketClient,
        IGeminiMessageProcessor messageProcessor,
        IGeminiSetupConfigBuilder setupConfigBuilder
        ) : IDisposable
    {

        private const string GeminiUrl = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent";
        private string _apiKey = apiKey;
        private readonly SemaphoreSlim _wsSemaphore = new(1, 1);
        private DateTime _sessionEndTime;
        private string _pendingResumeText;
        private string _currentResumeText;
        private readonly Stopwatch _latencyWatch = new();
        private bool _isWaitingForFirstResponse = false;
        private bool _aiIsSpeaking = false;
        private bool _isDisposed = false;
        private bool _isReceiveLoopRunning = false;
        private readonly object _receiveLoopLock = new();
        private readonly IWebSocketClient _webSocketClient = webSocketClient;
        private readonly IGeminiMessageProcessor _messageProcessor = messageProcessor;

        public event Func<byte[], Task>? OnAudioReceived;

        public bool IsConnected => _webSocketClient?.State == WebSocketState.Open;
        public bool HasResume => !string.IsNullOrWhiteSpace(_pendingResumeText) || !string.IsNullOrWhiteSpace(_currentResumeText);
        public async Task ConnectAsync()
        {
            AttachMessageProcessorHandlersOnce();

            if (string.IsNullOrEmpty(_apiKey)) throw new Exception("API Key is missing");
            if (_isDisposed) throw new ObjectDisposedException(nameof(GeminiLiveService));


            if (_webSocketClient?.State == WebSocketState.Open)
            {
                logger.LogInformation("WebSocket already connected");
                return;
            }

            await _wsSemaphore.WaitAsync();
            try
            {
                if (_webSocketClient?.State == WebSocketState.Open)
                {
                    logger.LogInformation("WebSocket already connected");
                    return;
                }

                await _webSocketClient.ConnectAsync(new Uri($"{GeminiUrl}?key={_apiKey}"), CancellationToken.None);
                logger.LogInformation("WebSocket connected, initializing session...");
            }
            catch (Exception ex)
            {
                logger.LogError($"Fail for connect in API: {ex.Message}");
                try { _webSocketClient.DisposeAsync().AsTask().Wait(); } catch { }
                throw;
            }
            finally
            {
                _wsSemaphore.Release();
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(_pendingResumeText))
                {
                    _currentResumeText = _pendingResumeText;
                    logger.LogInformation("Resume loaded into interview context");
                }

                await InitializeSessionAsync();

                lock (_receiveLoopLock)
                {
                    if (!_isReceiveLoopRunning)
                    {
                        _isReceiveLoopRunning = true;
                        _ = Task.Run(async () =>
                        {
                            await ReceiveLoopAsync();
                            lock (_receiveLoopLock)
                            {
                                _isReceiveLoopRunning = false;
                            }
                        });
                    }
                    else
                    {
                        logger.LogWarning("Receive loop already running, skipping duplicate");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to initialize session: {ex.Message}");
                await DisconnectAsync();
                throw;
            }
        }

        private bool _handlersAttached = false;
        private void AttachMessageProcessorHandlersOnce()
        {
            if (_handlersAttached) return;
            _handlersAttached = true;

            _messageProcessor.OnAudioReceived += async (bytes) =>
            {
                try
                {
                    if (_isWaitingForFirstResponse)
                    {
                        _latencyWatch.Stop();
                        _aiIsSpeaking = true;
                        logger.LogInformation($"Ping: {_latencyWatch.ElapsedMilliseconds}ms");
                        _isWaitingForFirstResponse = false;
                    }

                    if (OnAudioReceived != null)
                        await OnAudioReceived.Invoke(bytes);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error handling audio from message processor");
                }
            };

            _messageProcessor.OnAITextReceived += async (text) =>
            {
                try
                {
                    await interviewLogger.LogAsync("Brendha (AI): ", text);
                    logger.LogInformation($"Brendha(IA): {text}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error handling toolCall from message processor");
                }
            };

            _messageProcessor.OnAITextChunkReceived += async (chunk) =>
            {
                try
                {
                    await hubContext.Clients.All.SendAsync("ReceiveAIQuestion", chunk);
                }
                catch (Exception ex) { logger.LogError(ex, "Error sending AI text chunk to front"); }
            };


            _messageProcessor.OnUserTextReceived += async (text) =>
            {
                try
                {
                    await interviewLogger.LogAsync("Candidate: ", text);
                    logger.LogInformation($"Candidate: {text}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error handling user text from message processor");
                }
            };

            _messageProcessor.OnTurnComplete += async () =>
            {
                try
                {
                    _aiIsSpeaking = false;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error handling turn complete from message processor");
                }
                await Task.CompletedTask;
            };
        }

        public async Task SetResumeAsync(string resumeJson)
        {
            if (string.IsNullOrWhiteSpace(resumeJson))
            {
                logger.LogWarning("Received empty resume JSON.");
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(resumeJson);
                if (doc.RootElement.TryGetProperty("resumeText", out var el) ||
                    doc.RootElement.TryGetProperty("resume_text", out el))
                {
                    _pendingResumeText = el.GetString()!;
                    logger.LogInformation("Resume set successfully. Ready to start interview");

                    await hubContext.Clients.All.SendAsync("ResumeReceived", new { success = true });
                }
                else
                {
                    logger.LogWarning("Property 'resumeText' not found in JSON");
                    await hubContext.Clients.All.SendAsync("ResumeReceived", new { success = false, error = "Invalid format" });
                }
            }
            catch (JsonException ex)
            {
                logger.LogError($"Invalid JSON format for resume: {ex.Message}");
                await hubContext.Clients.All.SendAsync("ResumeReceived", new { success = false, error = "Invalid JSON" });
            }


        }
        public async Task LogSystemMessage(string text) => await interviewLogger.LogAsync("System:", text);
        public async Task SendAudioChunkAsync(byte[] audioData)
        {
            if (_webSocketClient?.State != WebSocketState.Open) return;

            var payload = new RealtimeInputRequest(new RealtimeInput(new[]
            {
                new MediaChunk("audio/pcm", Convert.ToBase64String(audioData))
            }));

            if (!_aiIsSpeaking) { _latencyWatch.Restart(); _isWaitingForFirstResponse = true; }
            await SendJsonAsync(JsonSerializer.Serialize(payload));
        }
        public double GetTimeRemaining()
        {
            var remaining = (_sessionEndTime - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? remaining : 0;
        }
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            contextManager?.Dispose();
            try { _webSocketClient?.DisposeAsync().AsTask().Wait(); } catch { }
            try { _wsSemaphore?.Dispose(); } catch { }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                logger.LogInformation("Closing Gemini Connection");

                await _wsSemaphore.WaitAsync();
                try
                {
                    if (_webSocketClient?.State == WebSocketState.Open)
                    {
                        await _webSocketClient.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Interview ended by user",
                            CancellationToken.None);
                    }

                    try { await _webSocketClient.DisposeAsync(); } catch { }
                }
                finally
                {
                    _wsSemaphore.Release();
                }

                await interviewLogger.LogAsync("System", "Interview ended");
                logger.LogInformation("Gemini connection closed successfully");
            }

            catch (Exception ex)
            {
                logger.LogError($"Error closing Gemini connection: {ex.Message}");
            }
        }
        private async Task InitializeSessionAsync()
        {
            _sessionEndTime = DateTime.UtcNow.AddMinutes(10);
            await hubContext.Clients.All.SendAsync("SessionStarted", GetTimeRemaining());

            var job = JobMockService.GetCurrentJob();

            var reconnectionContext = await contextManager.BuildReconnectionContextAsync();
            var hasContext = !string.IsNullOrWhiteSpace(reconnectionContext);

            if (hasContext)
            {
                logger.LogInformation($"Reconnection with context {reconnectionContext.Length}  chars");
                await interviewLogger.LogAsync("System", $"Reconnection with context recovery from history");
            }
            else { logger.LogInformation("Starting fresh interview"); }

            var config = setupConfigBuilder.Build(job, _currentResumeText, reconnectionContext);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var configJson = JsonSerializer.Serialize(config, options);
            await SendJsonInternalAsync(configJson);

            logger.LogInformation("New sesion started");
        }

        private async Task SendJsonInternalAsync(string json)
        {
            if (_webSocketClient?.State != WebSocketState.Open) return;

            await _webSocketClient.SendTextAsync(json, CancellationToken.None);
        }

        private async Task ReceiveLoopAsync()
        {
            while (!_isDisposed)
            {
                try
                {
                    if (_webSocketClient?.State != WebSocketState.Open)
                        break;

                    var (result, buffer) = await _webSocketClient.ReceiveAsync(CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        logger.LogWarning($"Google closed connection. Status: {result.CloseStatus}, Desc: {result.CloseStatusDescription}");

                        if (result.CloseStatus == WebSocketCloseStatus.NormalClosure)
                        {
                            logger.LogInformation("Connection closed normally by user or server");
                            break;
                        }

                        if (!reconnectionManager.ShouldAttemptReconnection(result.CloseStatus))
                        {
                            logger.LogError("Non-recoverable error: {Status}. Will NOT reconnect.", result.CloseStatus);

                            await hubContext.Clients.All.SendAsync("FatalError", new
                            {
                                status = result.CloseStatus?.ToString(),
                                description = result.CloseStatusDescription,
                                message = "Non-recoverable error, please restart"
                            });

                            await interviewLogger.LogAsync("System",
                                $"Erro fatal: {result.CloseStatus} - {result.CloseStatusDescription}");

                            break;
                        }

                        logger.LogInformation("Attempting automatic reconnection...");

                        bool reconnected = await reconnectionManager.TryReconnectAsync(async () =>
                        {
                            await ConnectAsync();
                        });

                        if (reconnected) continue;
                        else break;
                    }

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await _messageProcessor.ProcessAsync(json);
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error in receive loop: {ex.Message}");
                    break;
                }
            }

            try { await _webSocketClient.DisposeAsync(); } catch { }

            logger.LogInformation("Receive loop ended");
        }

        private async Task SendJsonAsync(string json)
        {
            await SendJsonInternalAsync(json);
        }

    }
}
