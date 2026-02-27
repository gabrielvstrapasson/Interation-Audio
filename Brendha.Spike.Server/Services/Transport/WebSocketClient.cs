using Brendha.Spike.Server.Interfaces.Transport;
using System.Net.WebSockets;
using System.Text;

namespace Brendha.Spike.Server.Services.Transport
{
    public class WebSocketClient : IWebSocketClient
    {
        private ClientWebSocket _ws = new();
        private SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);
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
            try
            {
                if (_sendSemaphore.Wait(0)) { _sendSemaphore.Release(); }
            }
            catch (ObjectDisposedException) { _sendSemaphore = new SemaphoreSlim(1, 1); }
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
            bool entered = false;
            try
            {
                try
                {
                    await _sendSemaphore.WaitAsync(cancellationToken);
                    entered = true;
                }
                catch (ObjectDisposedException)
                {
                    _sendSemaphore = new SemaphoreSlim(1, 1);
                    await _sendSemaphore.WaitAsync(cancellationToken);
                    entered = true;
                }
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                if (entered)
                    try { _sendSemaphore.Release(); } catch { }
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
