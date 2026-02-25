using System.Text.Json;

namespace Brendha.Spike.Server.Interfaces.Processing
{
    public interface IGeminiMessageProcessor
    {
        event Func<byte[], Task>? OnAudioReceived;
        event Func<string, Task>? OnAITextChunkReceived;
        event Func<string, Task>? OnAITextReceived;
        event Func<string, Task>? OnUserTextReceived;
        event Func<JsonElement, Task>? OnToolCallReceived;
        event Func<Task>? OnTurnComplete;
        Task ProcessAsync(string json);
    }
}
