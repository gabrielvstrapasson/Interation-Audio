using System.Text.Json;

namespace Brendha.Spike.Server.Interfaces.Processing
{
    public interface IGeminiMessageProcessor
    {
        event Func<byte[], Task>? OnAudioReceived;
        public event Func<string, Task>? OnAITextChunkReceived;
        public event Func<string, Task>? OnAITextReceived;
        public event Func<string, Task>? OnUserTextReceived;
        event Func<JsonElement, Task>? OnToolCallReceived;
        event Func<Task>? OnTurnComplete;
        Task ProcessAsync(string json);
    }
}
