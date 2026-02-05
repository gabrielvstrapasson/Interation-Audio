using Brendha.Spike.Core.Models.Gemini;
using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

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
        private DateTime _sessionEndTime;

        private readonly Stopwatch _latencyWatch = new();
        private bool _isWaitingForFirstResponse = false;
        private bool _aiIsSpeaking = false;

        public event Func<byte[], Task>? OnAudioReceived;

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
        public async Task ConnectAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) throw new Exception("API Key is missing");

            Dispose();
            _ws = new ClientWebSocket();

            try
            {
                _logger.LogInformation("Trying connect in Gemini..");
                await _ws.ConnectAsync(new Uri($"{GeminiUrl}?key={_apiKey}"), CancellationToken.None);

                await InitializeSessionAsync();
                await ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Fail for connect in API: {ex.Message}");
                throw;
            }

        }
        private async Task InitializeSessionAsync()
        {
            _sessionEndTime = DateTime.UtcNow.AddMinutes(10);
            await _hubContext.Clients.All.SendAsync("SessionStarted", GetTimeRemaining());

            var config = CreateSetupConfig();
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            await SendJsonAsync(JsonSerializer.Serialize(config, options));
            _logger.LogInformation("Start Session");
        }
        private GeminiSetupRequest CreateSetupConfig()
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Você é Brendha, Recrutadora Técnica Senior.");
            prompt.AppendLine("REGRA DE OURO: Sua saída de texto deve ser A TRANSCRIÇÃO EXATA do que você está falando no áudio.");
            prompt.AppendLine("REGRA 0: NÃO GERE PENSAMENTOS INTERNOS. NÃO USE TEXTO EM NEGRITO.");
            prompt.AppendLine("REGRA 1: Sempre que entender o usuário, chame a ferramenta 'registrar_fala_usuario'.");
            prompt.AppendLine("REGRA 2: Só responda em áudio APÓS chamar a ferramenta.");

            var history = _interviewLogger.GetHistory();
            if (!string.IsNullOrEmpty(history))
            {
                prompt.AppendLine($"\n[RECONEXÃO - HISTÓRICO]:\n{history}\nContinue a entrevista.");
            }

            return new GeminiSetupRequest(new GeminiSetup(
                Model: "models/gemini-2.5-flash-native-audio-latest",
                GenerationConfig: new GenerationConfig(
                    ResponseModalities: new[] { "AUDIO" },
                    SpeechConfig: new SpeechConfig(new VoiceConfig(new PrebuiltVoiceConfig("Aoede")))
                    ),
                    SystemInstruction: new SystemInstruction(new[] { new Part(prompt.ToString()) }),
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
            while (_ws?.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning($"Google closed connection. Status: {result.CloseStatus}, Desc: {result.CloseStatusDescription}");
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessIncomingMessageAsync(json);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in receive loop: {ex.Message}");
                    break;
                }
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
            if (_ws?.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        private void DisposeWebScoket()
        {
            if (_ws != null) {
                try { _ws.Dispose(); } catch { }
                _ws = null;
            }
        }
        public void Dispose() => DisposeWebScoket();
    }
}
