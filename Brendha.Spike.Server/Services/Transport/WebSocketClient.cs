using Brendha.Spike.Server.Interfaces.Transport;
using System.Net.WebSockets;
using System.Text;

namespace Brendha.Spike.Server.Services.Transport
{
    public class WebSocketClient : IWebSocketClient
    {
        private ClientWebSocket _ws = new();
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);
        private const int BufferSize = 64 * 1024;

        public WebSocketState? State => _ws?.State;

        public async Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken = default)
        {
            if (_ws?.State == WebSocketState.Open)
            {
                await _ws.CloseAsync(closeStatus, statusDescription, cancellationToken);
            }
        }

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            try
            {
                _ws?.Dispose();
            }
            catch { }

            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(uri, cancellationToken);
        }

        public async Task<(WebSocketReceiveResult Result, byte[] Buffer)> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            var buffer = new byte[BufferSize];
            var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            return (result, buffer);
        }

        public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await _sendSemaphore.WaitAsync(cancellationToken);
            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            try { _ws?.Dispose(); } catch { }
            try { _sendSemaphore?.Dispose(); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
