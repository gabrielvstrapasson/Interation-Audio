using System.Net.WebSockets;

namespace Brendha.Spike.Server.Interfaces.Transport
{
    public interface IWebSocketClient : IAsyncDisposable
    {
        WebSocketState? State { get; }
        Task ConnectAsync (Uri uri, CancellationToken cancellationToken);
        Task SendTextAsync(string text, CancellationToken cancellationToken = default);
        Task<(WebSocketReceiveResult Result, byte[] Buffer)>  ReceiveAsync (CancellationToken cancellationToken = default);
        Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken = default);
    }
}
