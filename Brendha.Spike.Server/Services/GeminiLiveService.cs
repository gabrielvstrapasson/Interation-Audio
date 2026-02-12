using Brendha.Spike.Core.Models;
using Brendha.Spike.Core.Models.Gemini;
using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure.Interfaces;
using Brendha.Spike.Server.Services.Data;
using Brendha.Spike.Server.Services.Prompt;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Brendha.Spike.Server.Services
{
    public class GeminiLiveService : IDisposable
    {
        private const string GeminiUrl = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent";
        private const string TOOL_NAME = "registrar_fala_usuario";
        private readonly string _apiKey;
        private readonly ILogger<GeminiLiveService> _logger;
        public readonly IHubContext<InterviewHub> _hubContext;
        private readonly IInterviewLogger _interviewLogger;

        private ClientWebSocket _ws;
        private readonly SemaphoreSlim _wsSemaphore = new(1, 1);
        private DateTime _sessionEndTime;

        private string _pendingResumeText;
        private string _currentResumeText;

        private bool _allowAutoReconnect = false;

        private readonly Stopwatch _latencyWatch = new();
        private bool _isWaitingForFirstResponse = false;
        private bool _aiIsSpeaking = false;
        private bool _isDisposed = false;

        public event Func<byte[], Task>? OnAudioReceived;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public bool HasResume => !string.IsNullOrWhiteSpace(_pendingResumeText) || !string.IsNullOrWhiteSpace(_currentResumeText);

        public GeminiLiveService(string apiKey,
            ILogger<GeminiLiveService> logger,
            IHubContext<InterviewHub> hubContext,
            IInterviewLogger interviewLogger)
        {
            _apiKey = apiKey;
            _logger = logger;
            _hubContext = hubContext;
            _interviewLogger = interviewLogger;
        }
        public async Task ConnectAsync(bool enableAutoReconnect = false)
        {
            if (string.IsNullOrEmpty(_apiKey)) throw new Exception("API Key is missing");
            if (_isDisposed) throw new ObjectDisposedException(nameof(GeminiLiveService));

            if (_ws?.State == WebSocketState.Open)
            {
                _logger.LogInformation("WebSocket already connected");

                if (!enableAutoReconnect)
                {
                    await _hubContext.Clients.All.SendAsync("SessionStarted", GetTimeRemaining());
                }
                return;
            }

            await _wsSemaphore.WaitAsync();
            try
            {
                if (_ws?.State == WebSocketState.Open)
                {
                    _logger.LogInformation("WebSocket already connected");

                    if (!enableAutoReconnect)
                    {
                        await _hubContext.Clients.All.SendAsync("SessionStarted", GetTimeRemaining());
                    }
                    return;
                }

                DisposeWebSocketInternal();
                _ws = new ClientWebSocket();
                _allowAutoReconnect = enableAutoReconnect;

                _logger.LogInformation("Trying connect in Gemini..");
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
                _ = Task.Run(ReceiveLoopAsync);
                _logger.LogInformation("Gemini connection established successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize session: {ex.Message}");
                await DisconnectAsync();
                throw;
            }
        }

        public bool AllowAutoReconnect => _allowAutoReconnect;

     
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
                    _pendingResumeText = el.GetString();
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
        public async Task AppendUserHistory(string text) => await _interviewLogger.LogAsync("System:", text);
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
            DisposeWebSocketInternal();
            _wsSemaphore?.Dispose();
        }
        public async Task DisconnectAsync()
        {
            try
            {
                _logger.LogInformation("Closing Gemini Connection");
                _allowAutoReconnect = false;

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

            var config = CreateSetupConfig(job, _currentResumeText!);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var configJson = JsonSerializer.Serialize(config, options);
            await SendJsonInternalAsync(configJson);

            _logger.LogInformation("Start Session");
        }
        private async Task SendJsonInternalAsync(string json)
        {
            if (_ws?.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        private GeminiSetupRequest CreateSetupConfig(JobPosition job, string resumeText)
        {

            var history = _interviewLogger.GetHistory();
            var systemInstructionText = BrendhaPromptInterview.Build(job, resumeText, history);

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
                        if (part.TryGetProperty("text", out var textJson))
                        {
                            var text = textJson.GetString();

                            if (!string.IsNullOrWhiteSpace(text))
                                await _interviewLogger.LogAsync("Brendha (IA)", text);
                        }

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
                            if (OnAudioReceived != null) await OnAudioReceived.Invoke(Convert.FromBase64String(base64!));
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
                        break;
                    }

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessIncomingMessageAsync(json);
                }
                catch (WebSocketException wsEx)
                {
                    _logger.LogError($"WebSocket error in receive loop: {wsEx.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in receive loop: {ex.Message}");
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
                finally { _ws = null; }
            }
        }
    }
}
