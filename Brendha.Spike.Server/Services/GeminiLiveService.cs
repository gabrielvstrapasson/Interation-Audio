using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Brendha.Spike.Server.Services
{
    public class GeminiLiveService(string apiKey,
                                    ILogger<GeminiLiveService> logger) : IDisposable
    {
        private const string GeminiUrl = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent";
        private ClientWebSocket _ws;
        private readonly Stopwatch _latencyWatch = new();
        private bool _isWaitingForFirstResponse = false;
        private bool _aiIsSpeaking = false;

        public event Func<byte[], Task>? OnAudioReceived;
        public async Task ConnectAsync()
        {
            if (string.IsNullOrEmpty(apiKey)) throw new Exception("API Key não encontrada");
            if (_ws != null)
            {
                try { _ws.Dispose(); } catch { }
            }

            _ws = new ClientWebSocket();
            var uri = new Uri($"{GeminiUrl}?key={apiKey}");

            try
            {
                logger.LogInformation("Trying connect in Google WebSocket");
                await _ws.ConnectAsync(uri, CancellationToken.None);
                logger.LogInformation("Connect in API Gemini is successfull");

                await SendSystemInstruction();

                await ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                logger.LogError($"Fail for connect in API: {ex.Message}");
                throw;
            }

        }

        public void Dispose()
        {
            _ws.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "The socket is being closed", CancellationToken.None);
            GC.SuppressFinalize(this);
        }

        private async Task SendSystemInstruction()
        {
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
                    system_instruction = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = @"
                                    Você é Brendha, uma Recrutadora Técnica Sênior especializada em .NET e C#.
                                    Sua missão é entrevistar o candidato para uma vaga tech.
                                    
                                    Comportamento:
                                    0. Se apresente, fale sobre a vaga e depois inicie a entrevista.
                                    1. Seja profissional, mas cordial.
                                    2. Faça UMA pergunta técnica por vez. Não faça listas.
                                    3. Comece se apresentando e pedindo para o candidato se apresentar.
                                    4. Avalie as respostas. Se ele errar, corrija educadamente. Se acertar, aprofunde.
                                    5. Tópicos para cobrir: Async/Await, Injeção de Dependência, Garbage Collector e Arquitetura Limpa.
                                    6. Mantenha as respostas curtas e faladas (estilo conversacional)."
                            }
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(setupMessage);

            await SendJsonAsync(json);
            logger.LogInformation("Instructions send from Brendha.");

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
                        logger.LogWarning($"Google closed connection: {result.CloseStatus}");
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

                            if (root.TryGetProperty("serverContent", out var serverContent) &&
                                serverContent.TryGetProperty("modelTurn", out var modelTurn) &&
                                modelTurn.TryGetProperty("parts", out var parts))
                            {
                                foreach (var part in parts.EnumerateArray())
                                {
                                    if (part.TryGetProperty("text", out var textElement))
                                    {
                                        string texto = textElement.GetString();
                                        if (!string.IsNullOrWhiteSpace(texto))
                                            logger.LogWarning($"Brendha: {texto}");
                                    }

                                    if (part.TryGetProperty("inlineData", out var audioData))
                                    {
                                        if (_isWaitingForFirstResponse)
                                        {
                                            _latencyWatch.Stop();
                                            _aiIsSpeaking = true;
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            logger.LogWarning($"Ping: {_latencyWatch.ElapsedMilliseconds}");
                                            Console.ResetColor();
                                            _isWaitingForFirstResponse = false;
                                        }

                                        string base64audio = audioData.GetProperty("data").GetString();
                                        byte[] audioBytes = Convert.FromBase64String(base64audio);

                                        if (OnAudioReceived != null)
                                        {
                                            await OnAudioReceived.Invoke(audioBytes);
                                        }
                                    }
                                }
                            }
                            if (root.TryGetProperty("serverContent", out var sc) &&
                                sc.TryGetProperty("turnComplete", out var tc))
                            {
                                _aiIsSpeaking = false;
                                logger.LogInformation("Brendha: Finished talking.");
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error for read JSON {ex.Message}");
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        logger.LogError($"Google closed connection");
                        logger.LogError($"Status: {result.CloseStatus}");
                        logger.LogError($"Motivo: {result.CloseStatusDescription}");
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error in receive loop: {ex.Message}");
                    break;
                }
            }

        }

        private async Task SendJsonAsync(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
