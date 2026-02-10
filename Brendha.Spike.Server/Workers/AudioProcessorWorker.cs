using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Brendha.Spike.Server.Workers
{
    public class AudioProcessorWorker(ILogger<AudioProcessorWorker> logger,
                                      AudioStreamBuffer buffer,
                                      GeminiLiveService geminiLiveService,
                                      IHubContext<InterviewHub> hubContext) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            geminiLiveService.OnAudioReceived += async (audioBytes) =>
            {
                await hubContext.Clients.All.SendAsync("ReceiveAudio", audioBytes, stoppingToken);
            };

            var audioTask = Task.Run(async () =>
            {
                await foreach (var packet in buffer.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        if (geminiLiveService.IsConnected)
                        {
                            await geminiLiveService.SendAudioChunkAsync(packet.Payload);
                        }
                        else
                        {
                            logger.LogDebug("Gemini disconnected, skipping audio packet");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error sending audio chunk");
                    }
                }
            }, stoppingToken);

            var reconnectTask = Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {

                        if (!geminiLiveService.IsConnected && geminiLiveService.AllowAutoReconnect)
                        {
                            logger.LogWarning("Gemini disconnected (10min timeout), reconnecting...");
                            await geminiLiveService.ConnectAsync(enableAutoReconnect: true);
                            logger.LogInformation("Reconnected to Gemini successfully");
                        }

                        await Task.Delay(3000, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error in reconnection loop");
                        await Task.Delay(5000, stoppingToken);
                    }
                }
            }, stoppingToken);

            await Task.WhenAll(audioTask, reconnectTask);
        }
    }
}