using Brendha.Spike.Server.Interfaces.Processing;
using System.Text.Json;

namespace Brendha.Spike.Server.Services.Processing
{
    public class GeminiMessageProcessor(
        ILogger<GeminiMessageProcessor> logger) : IGeminiMessageProcessor
    {
        public event Func<byte[], Task>? OnAudioReceived;
        public event Func<JsonElement, Task>? OnToolCallReceived;
        public event Func<Task>? OnTurnComplete;

        public async Task ProcessAsync(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("toolCall", out var toolCall))
                {
                    if (OnToolCallReceived != null)
                    {
                        try { await OnToolCallReceived.Invoke(toolCall); }
                        catch (Exception ex) { logger.LogError(ex, "Error in OnToolCallReceived handler"); }
                    }

                }

                if (root.TryGetProperty("serverContent", out var serverContent))
                {
                    if (serverContent.TryGetProperty("modelTurn", out var modelTurn) &&
                        modelTurn.TryGetProperty("parts", out var parts))
                    {
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("inlineData", out var inlineData))
                            {
                                if (inlineData.TryGetProperty("data", out var dataEl))
                                {
                                    var base64 = dataEl.GetString();
                                    if (!string.IsNullOrEmpty(base64))
                                    {
                                        try
                                        {
                                            var bytes = Convert.FromBase64String(base64);
                                            if (OnAudioReceived != null)
                                            {
                                                try { await OnAudioReceived.Invoke(bytes); }
                                                catch (Exception ex) { logger.LogError(ex, "Error in OnAudioReceived handler"); }
                                            }
                                        }
                                        catch (FormatException fe)
                                        {
                                            logger.LogWarning(fe, "Invalid base64 audio string received");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (serverContent.TryGetProperty("turnComplete", out _))
                    {
                        if (OnTurnComplete != null)
                        {
                            try { await OnTurnComplete.Invoke(); }
                            catch (Exception ex) { logger.LogError(ex, "Error in OnTurnComplete handler"); }
                        }
                    }

                }
            }
            catch (JsonException jex)
            {
                logger.LogWarning(jex, "Malformed JSON received in GeminiMessageProcessor");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in GeminiMessageProcessor.ProcessAsync");
            }

        }
    }
}
