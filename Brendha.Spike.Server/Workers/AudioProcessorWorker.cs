using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Brendha.Spike.Server.Workers
{
    public class AudioProcessorWorker ( ILogger<AudioProcessorWorker> logger, 
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

            _ = Task.Run(async () =>
            {
                await foreach (var packet in buffer.ReadAllAsync(stoppingToken))
                {
                    await geminiLiveService.SendAudioChunkAsync(packet.Payload);
                }
            }, stoppingToken);


            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await geminiLiveService.ConnectAsync();
                    logger.LogWarning("Connection with Gemini is down, retrying...");
                }
                catch
                {
                    logger.LogCritical("Failed to connect to Gemini Live Service.");
                    await Task.Delay(5000, stoppingToken);
                }
                if (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(2000, stoppingToken);
                }
            }
        }
    }
}
