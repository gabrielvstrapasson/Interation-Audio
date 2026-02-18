using Brendha.Spike.Core.Models;
using Brendha.Spike.Core.Models.Gemini;
using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure.Interfaces;
using Brendha.Spike.Server.Services.Connection;
using Brendha.Spike.Server.Services.Context;
using Brendha.Spike.Server.Services.Data;
using Brendha.Spike.Server.Services.Prompt;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;

namespace Brendha.Spike.Server.Services
{
    public class GeminiLiveService : IDisposable
    {
        private const string GeminiUrl = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent";
        private const string TOOL_NAME = "registrar_fala_usuario";
        private const string TOOL_NAME_AI_QUESTION = "registrar_pergunta_ia";

        private readonly string _apiKey;
        private readonly ILogger<GeminiLiveService> _logger;
        public readonly IHubContext<InterviewHub> _hubContext;
        private readonly IInterviewLogger _interviewLogger;
        private readonly InterviewContextManager _contextManager;
        private readonly ReconnectionManager _reconnectionManager;
        private readonly SessionRenewalManager _sessionRenewalManager;
        private int _reconnectionAttemptCount = 0;

        private ClientWebSocket _ws;
        private readonly SemaphoreSlim _wsSemaphore = new(1, 1);
        private DateTime _sessionEndTime;

        private string _pendingResumeText;
        private string _currentResumeText;

        private readonly Stopwatch _latencyWatch = new();
        private bool _isWaitingForFirstResponse = false;
        private bool _aiIsSpeaking = false;
        private bool _isDisposed = false;
        private bool _isRenewingSession = false;
        private bool _isReceiveLoopRunning = false;
        private readonly object _receiveLoopLock = new();

        public event Func<byte[], Task>? OnAudioReceived;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public bool HasResume => !string.IsNullOrWhiteSpace(_pendingResumeText) || !string.IsNullOrWhiteSpace(_currentResumeText);

        public GeminiLiveService(string apiKey,
            ILogger<GeminiLiveService> logger,
            IHubContext<InterviewHub> hubContext,
            IInterviewLogger interviewLogger,
            InterviewContextManager contextManager,
            ReconnectionManager reconnectionManager,
            SessionRenewalManager sessionRenewalManager)
        {
            _apiKey = apiKey;
            _logger = logger;
            _hubContext = hubContext;
            _interviewLogger = interviewLogger;
            _contextManager = contextManager;
            _reconnectionManager = reconnectionManager;
            _sessionRenewalManager = sessionRenewalManager;
        }
        public async Task ConnectAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) throw new Exception("API Key is missing");
            if (_isDisposed) throw new ObjectDisposedException(nameof(GeminiLiveService));


            if (_ws?.State == WebSocketState.Open)
            {
                _logger.LogInformation("WebSocket already connected");
                return;
            }

            await _wsSemaphore.WaitAsync();
            try
            {
                if (_ws?.State == WebSocketState.Open)
                {
                    _logger.LogInformation("WebSocket already connected");
                    return;
                }

                DisposeWebSocketInternal();
                _ws = new ClientWebSocket();

                await _ws.ConnectAsync(new Uri($"{GeminiUrl}?key={_apiKey}"), CancellationToken.None);

                _logger.LogInformation("WebSocket connected, initializing session...");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Fail for connect in API: {ex.Message}");
                DisposeWebSocketInternal();
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
                    _logger.LogInformation("Resume loaded into interview context");
                }

                await InitializeSessionAsync();
                _contextManager.StartCheckpointTimer();


                _reconnectionManager.ResetState();

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
                        _logger.LogWarning("Receive loop already running, skipping duplicate");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize session: {ex.Message}");
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
                    _pendingResumeText = el.GetString()!;
                    _logger.LogInformation("Resume set successfully. Ready to start interview");

                    await _hubContext.Clients.All.SendAsync("ResumeReceived", new { success = true });
                }
                else
                {
                    _logger.LogWarning("Property 'resumeText' not found in JSON");
                    await _hubContext.Clients.All.SendAsync("ResumeReceived", new { success = false, error = "Invalid format" });
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Invalid JSON format for resume: {ex.Message}");
                await _hubContext.Clients.All.SendAsync("ResumeReceived", new { success = false, error = "Invalid JSON" });
            }


        }
        public async Task LogSystemMessage(string text) => await _interviewLogger.LogAsync("System:", text);
        public async Task SendAudioChunkAsync(byte[] audioData)
        {
            if (_ws?.State != WebSocketState.Open) return;

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

            _sessionRenewalManager?.Dispose();
            _reconnectionManager?.Dispose();
            _contextManager?.Dispose();
            DisposeWebSocketInternal();
            _wsSemaphore?.Dispose();
        }
        public async Task DisconnectAsync()
        {
            try
            {
                _logger.LogInformation("Closing Gemini Connection");

                _sessionRenewalManager.StopRenewalTimer();
                _reconnectionManager.CancelReconnection();
                _contextManager.StopCheckpointTimer();

                await _wsSemaphore.WaitAsync();
                try
                {
                    if (_ws?.State == WebSocketState.Open)
                    {
                        await _ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Interview ended by user",
                            CancellationToken.None);
                    }

                    DisposeWebSocketInternal();
                }
                finally
                {
                    _wsSemaphore.Release();
                }

                await _interviewLogger.LogAsync("System", "Interview ended");
                _logger.LogInformation("Gemini connection closed successfully");
            }

            catch (Exception ex)
            {
                _logger.LogError($"Error closing Gemini connection: {ex.Message}");
            }
        }
        private async Task InitializeSessionAsync()
        {
            _sessionEndTime = DateTime.UtcNow.AddMinutes(10);
            await _hubContext.Clients.All.SendAsync("SessionStarted", GetTimeRemaining());

            var job = JobMockService.GetCurrentJob();

            var reconnectionContext = await _contextManager.BuildReconnectionContextAsync();
            var hasContext = !string.IsNullOrWhiteSpace(reconnectionContext);

            var config = CreateSetupConfig(job, _currentResumeText, reconnectionContext);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var configJson = JsonSerializer.Serialize(config, options);
            await SendJsonInternalAsync(configJson);

            _logger.LogInformation(hasContext
                ? $"Session initialized with reconnection context (Renewal #{_sessionRenewalManager.RenewalCount})"
                : "New sesion started");

            if (!_sessionRenewalManager.IsRenewing)
            {
                _sessionRenewalManager.StartRenewalTimer(RenewSessionAsync);
            }

            else
            {
                _logger.LogDebug("Skipping renewal timer - renewal in progress");
            }
        }

        private async Task SendJsonInternalAsync(string json)
        {
            if (_ws?.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        private GeminiSetupRequest CreateSetupConfig(JobPosition job, string resumeText, string reconnectionContext = "")
        {
            var systemInstructionText = BrendhaPromptInterview.Build(job, resumeText);

            if (!string.IsNullOrWhiteSpace(reconnectionContext))
            {
                systemInstructionText += "\n Reconnection context for this session " +
                    reconnectionContext + "\n Use this information to maintain continuity in the interview. Do not repeat questions asked previously.";
            }

            return new GeminiSetupRequest(new GeminiSetup(
                Model: "models/gemini-2.5-flash-native-audio-latest",
                GenerationConfig: new GenerationConfig(
                    ResponseModalities: new[] { "AUDIO" },
                    SpeechConfig: new SpeechConfig(new VoiceConfig(new PrebuiltVoiceConfig("Aoede")))
                    ),
                    SystemInstruction: new SystemInstruction(new[] { new Part(systemInstructionText) }),
                    Tools: new List<Tool>
                    {
                        new Tool(new List<FunctionDeclaration>
                        {
                            new FunctionDeclaration(
                                Name: TOOL_NAME,
                                Description: "Record the candidate's speech.",
                                Parameters: new FunctionParameters(
                                    Type: "OBJECT",
                                    Properties: new Dictionary<string, PropertyDetail>
                                    {
                                        { "texto_entendido", new PropertyDetail("STRING", "Texto exato da fala.") }
                                    },
                                    Required: new[] { "texto_entendido" }
                                )
                            ),

                            new FunctionDeclaration(
                                Name: TOOL_NAME_AI_QUESTION,
                                Description: "Record your own question or statement to the candidate for logging purposes.",
                                Parameters: new FunctionParameters(
                                    Type: "OBJECT",
                                    Properties: new Dictionary<string, PropertyDetail>
                                    {
                                        {"minha_pergunta", new PropertyDetail("STRING", "The exact question or statement you just asked/said to the candidate") }
                                    },
                                    Required: new[] {"minha_pergunta"}
                                    )
                                )
                        })
                    }
                ));
        }
        private async Task ProcessIncomingMessageAsync(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("toolCall", out var toolCall)) await HandleToolCallAsync(toolCall);

            if (root.TryGetProperty("serverContent", out var serverContent))
            {
                if (serverContent.TryGetProperty("modelTurn", out var modelTurn) &&
                    modelTurn.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("inlineData", out var audioData))
                        {
                            if (_isWaitingForFirstResponse)
                            {
                                _latencyWatch.Stop();
                                _aiIsSpeaking = true;
                                _logger.LogInformation($"Ping: {_latencyWatch.ElapsedMilliseconds}ms");
                                _isWaitingForFirstResponse = false;
                            }

                            var base64 = audioData.GetProperty("data").GetString();
                            if (OnAudioReceived != null && !string.IsNullOrEmpty(base64)) await OnAudioReceived.Invoke(Convert.FromBase64String(base64!));
                        }
                    }
                }
                if (serverContent.TryGetProperty("turnComplete", out _)) _aiIsSpeaking = false;
            }
        }
        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[1024 * 64];

            while (!_isDisposed)
            {
                try
                {
                    if (_ws?.State != WebSocketState.Open)
                        break;

                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning($"Google closed connection. Status: {result.CloseStatus}, Desc: {result.CloseStatusDescription}");

                        // ✅ Detecta erros não recuperáveis
                        if (IsNonRecoverableError(result.CloseStatus))
                        {
                            _logger.LogError(
                                "Non-recoverable error detected: {Status}. Will NOT attempt reconnection.",
                                result.CloseStatus);

                            await _hubContext.Clients.All.SendAsync("FatalError", new
                            {
                                status = result.CloseStatus?.ToString(),
                                description = result.CloseStatusDescription,
                                message = "Configuration or content rejected by API. Please review and restart."
                            });

                            await _interviewLogger.LogAsync("System",
                                $"Fatal error: {result.CloseStatus} - {result.CloseStatusDescription}");

                            // Não tenta reconectar
                            break;
                        }

                        await _wsSemaphore.WaitAsync();
                        try
                        {
                            if (_ws?.State == WebSocketState.Open || _ws?.State == WebSocketState.CloseReceived)
                            {
                                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            }
                        }
                        finally
                        {
                            _wsSemaphore.Release();
                        }

                        await HandleDisconnectionAsync();
                        break;
                    }

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessIncomingMessageAsync(json);
                }
                catch (WebSocketException wsEx)
                {
                    _logger.LogError($"WebSocket error in receive loop: {wsEx.Message}");
                    await HandleDisconnectionAsync();
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in receive loop: {ex.Message}");
                    await HandleDisconnectionAsync();
                    break;
                }
            }

            await _wsSemaphore.WaitAsync();
            try
            {
                DisposeWebSocketInternal();
            }
            finally
            {
                _wsSemaphore.Release();
            }

            _logger.LogInformation("Receive loop ended");
        }

        // ✅ NOVO MÉTODO - Identifica erros não recuperáveis
        private bool IsNonRecoverableError(WebSocketCloseStatus? status)
        {
            if (!status.HasValue) return false;

            return status.Value switch
            {
                WebSocketCloseStatus.PolicyViolation => true, // Violação de política
                WebSocketCloseStatus.InvalidPayloadData => true, // Dados inválidos
                WebSocketCloseStatus.InvalidMessageType => true, // Tipo de mensagem inválido
                WebSocketCloseStatus.MandatoryExtension => true, // Extensão obrigatória ausente
                WebSocketCloseStatus.ProtocolError => true, // Erro de protocolo
                _ => false
            };
        }
        private async Task RenewSessionAsync()
        {
            if (_isRenewingSession || _isDisposed) return;

            _isRenewingSession = true;

            try
            {
                _logger.LogInformation("Starting proactive session renewal...");

                _contextManager.CreateCheckpoint();

                await _hubContext.Clients.All.SendAsync("SessionRenewing", new
                {
                    renewalNumber = _sessionRenewalManager.RenewalCount,
                    message = "Renewing session to continue interview..."
                });

                await _interviewLogger.LogAsync("System",
                    $"Session renewal #{_sessionRenewalManager.RenewalCount} - Maintaining conversation context");


                _sessionRenewalManager.StopRenewalTimer();
                await _wsSemaphore.WaitAsync();
                try
                {
                    if (_ws?.State == WebSocketState.Open)
                    {
                        await _ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Session renewal",
                            CancellationToken.None);
                    }
                    DisposeWebSocketInternal();
                }
                finally
                {
                    _wsSemaphore.Release();
                }


                await Task.Delay(3000);

                await ConnectAsync();

                await Task.Delay(2000);
                await _hubContext.Clients.All.SendAsync("SessionRenewed", new
                {
                    renewalNumber = _sessionRenewalManager.RenewalCount,
                    message = "Session renewed successfully. Continuing interview..."
                });

                _logger.LogInformation("Session renewed successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to renew session proactively");

                await HandleDisconnectionAsync();
            }
            finally
            {
                _isRenewingSession = false;
            }
        }

        private bool ShouldAttemptReconnection()
        {

            if (_isRenewingSession)
            {
                _logger.LogDebug("Skipping reconnection - session renewal in progress");
                return false;
            }

            if (_isDisposed)
            {
                return false;
            }

            return true;
        }
        private async Task HandleDisconnectionAsync()
        {
            if (_isDisposed || _isRenewingSession) return;

            _logger.LogWarning("Connection lost. Attempting to reconnect with context...");

            _reconnectionAttemptCount++; // ✅ Incrementa contador

            var success = await _reconnectionManager.TryReconnectAsync(
                reconnectAction: async () =>
                {
                    // ✅ Após 2 falhas, tenta SEM contexto
                    if (_reconnectionAttemptCount >= 3)
                    {
                        _logger.LogWarning("Too many failures, clearing context for next attempt");
                        await _contextManager.ClearContextAsync();
                    }

                    await ConnectAsync();
                },
                onCheckpoint: () => _contextManager.CreateCheckpoint(),
                shouldContinue: () => ShouldAttemptReconnection()
            );

            if (success)
            {
                _reconnectionAttemptCount = 0; // ✅ Reset no sucesso
            }
            else
            {
                _logger.LogError("Failed to reconnect after multiple attempts");
                await _interviewLogger.LogAsync("System", "Connection failed. Interview interrupted.");
            }
        }

        private async Task HandleToolCallAsync(JsonElement toolCall)
        {
            if (!toolCall.TryGetProperty("functionCalls", out var functionCalls)) return;

            var responses = new List<FunctionResponse>();

            foreach (var call in functionCalls.EnumerateArray())
            {
                var name = call.GetProperty("name").GetString();
                var id = call.GetProperty("id").GetString();

                if (name == TOOL_NAME)
                {
                    var args = call.GetProperty("args");
                    if (args.TryGetProperty("texto_entendido", out var txtElement))
                    {
                        var text = txtElement.GetString();
                        await _interviewLogger.LogAsync("Candidato:", text ?? "");
                        _logger.LogInformation($"Candidato: {text}");

                        await _contextManager.AddMessageAsync("Candidato", text);
                    }
                }
                else if (name == TOOL_NAME_AI_QUESTION)
                {
                    var args = call.GetProperty("args");
                    if (args.TryGetProperty("minha_pergunta", out var questionElement))
                    {
                        var question = questionElement.GetString();
                        await _interviewLogger.LogAsync("Brendha (IA)", question ?? "");
                        _logger.LogInformation($"Brendha (IA): {question}");
                        await _contextManager.AddMessageAsync("Brendha (IA)", question);

                        if (!string.IsNullOrEmpty(question))
                        {
                            await _hubContext.Clients.All.SendAsync("ReceiveAIQuestion", question);
                        }
                    }
                }

                responses.Add(new FunctionResponse(new { result = "ok" }, id!));
            }
            var payload = new ToolCallResponse(new ToolResponsePayload(responses));
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            await SendJsonAsync(JsonSerializer.Serialize(payload, options));
        }
        private async Task SendJsonAsync(string json)
        {
            await _wsSemaphore.WaitAsync();
            try
            {
                await SendJsonInternalAsync(json);
            }
            finally
            {
                _wsSemaphore.Release();
            }
        }
        private void DisposeWebSocketInternal()
        {
            if (_ws != null)
            {
                try { _ws.Dispose(); }
                catch { }
                finally { _ws = null!; }
            }
        }
    }
}
