using Brendha.Spike.Server.Interfaces.Processing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Brendha.Spike.Server.Services.Processing
{
    public class GeminiMessageProcessor(
        ILogger<GeminiMessageProcessor> logger) : IGeminiMessageProcessor
    {
        public event Func<byte[], Task>? OnAudioReceived;
        public event Func<string, Task>? OnAITextChunkReceived;

        public event Func<string, Task>? OnAITextReceived;

        public event Func<string, Task>? OnUserTextReceived;
        public event Func<Task>? OnTurnComplete;
        public event Func<JsonElement, Task>? OnToolCallReceived;

        private readonly StringBuilder _aiTextBuffer = new();
        private string _lastUserText = string.Empty;

        public async Task ProcessAsync(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

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
                                    if (!string.IsNullOrEmpty(base64) && OnAudioReceived != null)
                                    {
                                        try
                                        {
                                            var bytes = Convert.FromBase64String(base64);
                                            await OnAudioReceived.Invoke(bytes);
                                        }
                                        catch (Exception ex) { logger.LogWarning("Error to process audio " + ex.Message); }
                                    }
                                }
                            }
                            if (part.TryGetProperty("text", out var textEl))
                            {
                                var text = textEl.GetString();
                                if (!string.IsNullOrEmpty(text) && !text.TrimStart().StartsWith("**"))
                                {
                                    _aiTextBuffer.Append(text);

                                    if (OnAITextChunkReceived != null)
                                    {
                                        try
                                        {
                                            var cleanChunk = Regex.Unescape(text);
                                            await OnAITextChunkReceived.Invoke(cleanChunk);
                                        }
                                        catch (Exception ex) { logger.LogWarning("Error sending text chunk: " + ex.Message); }
                                    }
                                }
                            }
                        }
                    }

                    if (serverContent.TryGetProperty("inputTranscription", out var inputTrans) ||
                        serverContent.TryGetProperty("input_transcription", out inputTrans))
                    {
                        if (inputTrans.TryGetProperty("text", out var userTextEl))
                        {
                            var userText = userTextEl.GetString();
                            if (!string.IsNullOrEmpty(userText))
                            {
                                _lastUserText = userText;
                            }
                        }
                        else if (inputTrans.TryGetProperty("parts", out var inputParts))
                        {
                            var sb = new StringBuilder();
                            foreach (var part in inputParts.EnumerateArray())
                            {
                                if (part.TryGetProperty("text", out var ptText))
                                {
                                    var txt = ptText.GetString();
                                    if (!string.IsNullOrWhiteSpace(txt))
                                    {
                                        sb.Append(txt);
                                    }
                                }
                            }
                            if (sb.Length > 0)
                            {
                                _lastUserText = sb.ToString();
                            }
                        }
                    }

                    if (serverContent.TryGetProperty("outputTranscription", out var outputTrans) ||
                        serverContent.TryGetProperty("output_transcription", out outputTrans))
                    {
                        if (outputTrans.TryGetProperty("text", out var aiTextEl))
                        {
                            var aiText = aiTextEl.GetString();
                            if (!string.IsNullOrEmpty(aiText) && !aiText.TrimStart().StartsWith("**"))
                            {
                                _aiTextBuffer.Append(aiText);

                                if (OnAITextChunkReceived != null)
                                {
                                    try { await OnAITextChunkReceived.Invoke(aiText); }
                                    catch (Exception ex) { logger.LogWarning("Error sending text chunk: " + ex.Message); }
                                }
                            }
                        }
                    }

                    if (serverContent.TryGetProperty("interrupted", out var interrupted) && interrupted.GetBoolean())
                    {
                        await FlushAIBufferAsync();
                    }

                    if (serverContent.TryGetProperty("turnComplete", out _))
                    {
                        await FlushUserBufferAsync();
                        await FlushAIBufferAsync();

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

        private async Task FlushUserBufferAsync()
        {
            var fullUserText = _lastUserText.Trim();
            if (fullUserText.Length > 0)
            {

                if (fullUserText == "<noise>" || fullUserText == ".")
                {
                    _lastUserText = string.Empty;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(fullUserText) && OnUserTextReceived != null)
                {
                    try { await OnUserTextReceived.Invoke(fullUserText); }
                    catch (Exception ex) { logger.LogError(ex, "Error flushing user text buffer: " + ex.Message); }
                }

                _lastUserText = string.Empty;
            }
        }

        private async Task FlushAIBufferAsync()
        {
            if (_aiTextBuffer.Length > 0)
            {
                var fullAiText = _aiTextBuffer.ToString().Trim();

                fullAiText = Regex.Unescape(fullAiText);

                if (!string.IsNullOrWhiteSpace(fullAiText) && OnAITextReceived != null)
                {
                    try { await OnAITextReceived.Invoke(fullAiText); }
                    catch (Exception ex) { logger.LogError(ex, "Error flushing AI text buffer: " + ex.Message); }
                }

                _aiTextBuffer.Clear();
            }
        }
    }
}