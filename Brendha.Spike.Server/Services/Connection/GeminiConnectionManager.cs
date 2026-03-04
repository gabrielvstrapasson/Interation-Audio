using Brendha.Interview.Server.Interfaces.Connection;
using Brendha.Interview.Server.Interfaces.Transport;
using Brendha.Interview.Server.Services.Gemini;
using System.Net.WebSockets;
using System.Text;

namespace Brendha.Interview.Server.Services.Connection
{
    public class GeminiConnectionManager(
        ILogger<GeminiConnectionManager> logger,
        IWebSocketClient webSocketClient,
        IGeminiReconnectionManager reconnectionManager,
        IConfiguration configuration
        ) : IGeminiConnectionManager
    {

        private readonly ILogger<GeminiConnectionManager> _logger = logger;
        private readonly IWebSocketClient _webSocketClient = webSocketClient;
        private readonly IGeminiReconnectionManager _reconnectionManager = reconnectionManager;

        private readonly string _geminiStreamingUrl = configuration["Gemini:StreamingUrl"];
        private readonly string _apiKey = configuration["Gemini:ApiKey"];
        
        private readonly SemaphoreSlim _wsSemaphore = new(1, 1);
        private bool _isDisposed = false;
        private CancellationTokenSource _cts = new();

        public event Func<string, Task>? OnMessageReceived;
        public event Func<WebSocketCloseStatus?, string?, Task>? OnConnectionClosed;

        public bool IsConnected => _webSocketClient?.State == WebSocketState.Open;

        public async Task ConnectAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) throw new InvalidOperationException("API Key is missing");
            if (_isDisposed) throw new ObjectDisposedException(nameof(GeminiConnectionManager));

            if (IsConnected)
            {
                logger.LogInformation("WebSocket already connected");
                return;
            }

            await _wsSemaphore.WaitAsync(_cts.Token);
            try
            {
                if (IsConnected) return;

                await _webSocketClient.ConnectAsync(new Uri($"{_geminiStreamingUrl}?key={_apiKey}"), _cts.Token);
                logger.LogInformation("WebSocket connected, initializing session...");

                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            finally
            {
                _wsSemaphore.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            await _wsSemaphore.WaitAsync();
            try
            {
                if (IsConnected)
                {
                    await _webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect requested", CancellationToken.None);
                }
                if (!_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                }
            }
            finally
            {
                _wsSemaphore.Release();
            }
        }

        public async Task SendJsonAsync(string json)
        {
            if (!IsConnected)
            {
                _logger.LogWarning("Attempted to send data while disconnected.");
                return;
            }
            await _webSocketClient.SendTextAsync(json, _cts.Token);
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            _logger.LogInformation("Receive loop started.");
            while (!token.IsCancellationRequested && IsConnected)
            {
                try
                {
                    var (result, buffer) = await _webSocketClient.ReceiveAsync(token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation($"Google closed connection. Status: {result.CloseStatus}, Description: {result.CloseStatusDescription}");
                        if (OnConnectionClosed != null)
                        {
                            await OnConnectionClosed.Invoke(result.CloseStatus, result.CloseStatusDescription);
                        }
                        if (_reconnectionManager.ShouldAttemptReconnection(result.CloseStatus))
                        {
                            _logger.LogInformation("Attempting automatic reconnection...");
                            bool reconnected = await _reconnectionManager.TryReconnectAsync(ConnectAsync, token);
                            if (reconnected) continue;
                        }
                        break;
                    }

                    if (OnMessageReceived != null)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await OnMessageReceived.Invoke(json);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Receive loop cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in receive loop.");
                    if (OnConnectionClosed != null)
                    {
                        await OnConnectionClosed.Invoke(WebSocketCloseStatus.InternalServerError, ex.Message);
                    }
                    break;
                }
            }
            _logger.LogInformation("Receive loop ended.");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            if (!_cts.IsCancellationRequested) _cts.Cancel();
            _cts.Dispose();
            _wsSemaphore.Dispose();
            _webSocketClient.DisposeAsync().AsTask().Wait();
        }
    }
}
