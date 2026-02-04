using Brendha.Spike.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Brendha.Spike.Server.Services
{
    public class GeminiLiveService : IDisposable
    {
        private const string GeminiUrl = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent";

        private readonly string _apiKey;
        private readonly ILogger<GeminiLiveService> _logger;
        private ClientWebSocket _ws;

        private readonly Stopwatch _latencyWatch = new();
        private bool _isWaitingForFirstResponse = false;
        private bool _aiIsSpeaking = false;

        private readonly StringBuilder _conversationHistory = new();
        private readonly string _logFilePath;
        private readonly IHubContext<InterviewHub> _hubContext;
        private DateTime _sessionEndTime;

        public event Func<byte[], Task>? OnAudioReceived;

        public GeminiLiveService(string apiKey, 
            ILogger<GeminiLiveService> logger,
            IHubContext<InterviewHub> hubContext)
        {
            _apiKey = apiKey;
            _logger = logger;
            _hubContext = hubContext;

            string executionPath = AppContext.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(executionPath, "../../../"));
            string logFolder = Path.Combine(projectRoot, "logs_interview");

            if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

            _logFilePath = Path.Combine(logFolder, $"interview_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        }
        public async Task ConnectAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) throw new Exception("API Key não encontrada");
            if (_ws != null)
            {
                try { _ws.Dispose(); } catch { }
            }

            _ws = new ClientWebSocket();
            var uri = new Uri($"{GeminiUrl}?key={_apiKey}");

            try
            {
                _logger.LogInformation("Trying connect in Google WebSocket");
                await _ws.ConnectAsync(uri, CancellationToken.None);
                _logger.LogInformation("Connect in API Gemini is successfull");

                _sessionEndTime = DateTime.UtcNow.AddMinutes(10);
                var secondsLeft = (_sessionEndTime - DateTime.UtcNow).TotalSeconds;

                await _hubContext.Clients.All.SendAsync("SessionStarted", 600);
                await SendSystemInstruction();

                await ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Fail for connect in API: {ex.Message}");
                throw;
            }

        }

        public void Dispose() => _ws.Dispose();

        public async Task AppendUserHistory(string text)
        {
            _logger.LogInformation($"Candidato: {text}");
            await SaveToLogAsync("Candidato", text);
        }

        public async Task SendAudioChunkAsync(byte[] audioData)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;

            var message = new
            {
                realtime_input = new
                {
                    media_chunks = new[]
                    {
                        new
                        {
                            mime_type = "audio/pcm",
                            data = Convert.ToBase64String(audioData)
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(message);
            if (!_aiIsSpeaking)
            {
                _latencyWatch.Restart();
                _isWaitingForFirstResponse = true;
            }
            await SendJsonAsync(JsonSerializer.Serialize(message));
        }
        public double GetTimeRemaining()
        {
            var remaining = (_sessionEndTime - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? remaining : 0;
        }

        private async Task SendSystemInstruction()
        {

            var baseInstruction = @"
                Você é Brendha, Recrutadora Técnica Senior.
                
                REGRAS CRÍTICAS DE FUNCIONAMENTO:
                1. MUTE SEUS PENSAMENTOS: Não gere texto de saída explicando o que você vai fazer (ex: **Initiating**, I've registered...).
                2. ESCUTA ATIVA: Assim que o usuário terminar de falar e você entender, CHAME IMEDIATAMENTE a função 'registrar_fala_usuario' com o texto exato.
                3. SEQUÊNCIA: 
                   - Ouvir Áudio -> Chamar Tool (registrar_fala_usuario) -> Gerar Áudio de Resposta.
                
                Comportamento:
                - Seja profissional e direta.
                - Respostas curtas (estilo conversa de voz).
                - Uma pergunta técnica por vez.";

            if (_conversationHistory.Length > 0)
            {
                baseInstruction += $"\n\n[IMPORTANTE: Ocorreu uma reconexão de rede]\n" +
                                   $"Abaixo está o histórico da conversa até agora. " +
                                   $"NÃO SE APRESENTE NOVAMENTE. Continue a entrevista de onde parou." +
                                   $"{_conversationHistory}";

                _logger.LogWarning("Reconectando com histórico de conversa.");
            }

            var setupMessage = new
            {
                setup = new
                {
                    model = "models/gemini-2.5-flash-native-audio-latest",
                    generation_config = new
                    {
                        response_modalities = new[] { "AUDIO" },
                        speech_config = new
                        {
                            voice_config = new
                            {
                                prebuilt_voice_config = new
                                {
                                    voice_name = "Aoede"
                                }
                            }
                        }
                    },
                    system_instruction = new { parts = new[] { new { text = baseInstruction } } },
                    tools = new[]
                    {
                        new
                        {
                            function_declarations = new[]
                            {
                                new
                                {
                                    name = "registrar_fala_usuario",
                                    description = "Registra o que o candidato falou para a log da entrevista",
                                    parameters = new
                                    {
                                        type = "OBJECT",
                                        properties = new
                                        {
                                            texto_entedido = new {type = "STRING", description = "A transição exata do que p candidato disse."}
                                        },
                                        required = new[] {"texto_entedido"}
                                    }
                                }
                            }
                        }
                    }
                }
            };

            await SendJsonAsync(JsonSerializer.Serialize(setupMessage));
            _logger.LogInformation("Instructions send from Brendha.");

        }
        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[1024 * 64];
            while (_ws == null || _ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning($"Google closed connection: {result.CloseStatus}");
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text ||
                        result.MessageType == WebSocketMessageType.Binary)
                    {
                        string responseJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        try
                        {
                            using var doc = JsonDocument.Parse(responseJson);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("toolCall", out var toolCall)) await HandleToolCall(toolCall);

                            if (root.TryGetProperty("serverContent", out var serverContent) &&
                                serverContent.TryGetProperty("modelTurn", out var modelTurn) &&
                                modelTurn.TryGetProperty("parts", out var parts))
                            {
                                foreach (var part in parts.EnumerateArray())
                                {
                                    if (part.TryGetProperty("text", out var textElement))
                                    {
                                        string texto = textElement.GetString() ?? "";

                                        bool ehPensamento = texto.StartsWith("**") ||
                                                    texto.StartsWith("I've") ||
                                                    texto.StartsWith("I'm") ||
                                                    texto.StartsWith("Thinking") ||
                                                    texto.StartsWith("Initiating");

                                        if (!string.IsNullOrWhiteSpace(texto) && !ehPensamento)
                                        {

                                            _logger.LogWarning($"Brendha: {texto}");
                                            await SaveToLogAsync("Brendha (IA): ", texto);
                                        }
                                    }

                                    if (part.TryGetProperty("inlineData", out var audioData))
                                    {
                                        if (_isWaitingForFirstResponse)
                                        {
                                            _latencyWatch.Stop();
                                            _aiIsSpeaking = true;
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            _logger.LogWarning($"Ping: {_latencyWatch.ElapsedMilliseconds}");
                                            Console.ResetColor();
                                            _isWaitingForFirstResponse = false;
                                        }

                                        string base64audio = audioData.GetProperty("data").GetString();
                                        byte[] audioBytes = Convert.FromBase64String(base64audio);

                                        if (OnAudioReceived != null) await OnAudioReceived.Invoke(audioBytes);
                                    }
                                }
                            }
                            if (root.TryGetProperty("serverContent", out var sc) &&
                                sc.TryGetProperty("turnComplete", out var tc))
                            {
                                _aiIsSpeaking = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error for read JSON {ex.Message}");
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogError($"Google closed connection");
                        _logger.LogError($"Status: {result.CloseStatus}");
                        _logger.LogError($"Motivo: {result.CloseStatusDescription}");
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in receive loop: {ex.Message}");
                    break;
                }
            }

        }
        private async Task HandleToolCall(JsonElement toolCall)
        {
            if (toolCall.TryGetProperty("functionCalls", out var functionCalls))
            {
                foreach (var call in functionCalls.EnumerateArray())
                {
                    string name = call.GetProperty("name").GetString();
                    string callId = call.GetProperty("id").GetString();

                    if (name == "registrar_fala_usuario")
                    {
                        var args = call.GetProperty("args");
                        if (args.TryGetProperty("texto_entendido", out var textoElement))
                        {
                            string textoEntendido = textoElement.GetString();
                            _logger.LogInformation($"Candidato: {textoEntendido}");
                            await SaveToLogAsync("Candidato: ", textoEntendido);
                        }
                    }

                    var toolResponse = new
                    {
                        tool_response = new
                        {
                            function_responses = new[]
                            {
                                new
                                {
                                    response = new { result = "ok" },
                                    id = callId
                                }
                            }
                        }
                    };
                    await SendJsonAsync(JsonSerializer.Serialize(toolResponse));
                }
            }
        }
        private async Task SendJsonAsync(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task SaveToLogAsync(string role, string text)
        {
            lock (_conversationHistory)
            {
                _conversationHistory.AppendLine($"{role}: {text}");
            }
            try
            {
                var logLine = $"[{DateTime.Now:HH:mm:ss}] {role}: {text}{Environment.NewLine}";
                await File.AppendAllTextAsync(_logFilePath, logLine);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao salvar log em disco: {ex.Message}");
            }

        }
    }
}
