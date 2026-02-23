using System.Text.Json;

namespace Brendha.Spike.Server.Interfaces.Processing
{
    public interface IGeminiMessageProcessor
    {
        event Func<byte[], Task>? OnAudioReceived;
        event Func<JsonElement, Task>? OnToolCallReceived;
        event Func<Task>? OnTurnComplete;
        Task ProcessAsync(string json);
    }
}
