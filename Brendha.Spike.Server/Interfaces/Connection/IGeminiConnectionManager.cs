using System.Net.WebSockets;

namespace Brendha.Spike.Server.Interfaces.Connection
{
    public interface IGeminiConnectionManager : IDisposable
    {
        bool IsConnected { get; }
        public event Func<string, Task>? OnMessageReceived;
        public event Func<WebSocketCloseStatus?, string?, Task>? OnConnectionClosed;

        Task ConnectAsync();
        Task DisconnectAsync();
        Task SendJsonAsync(string json);
    }
}
