using Brendha.Spike.Server.Hubs;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Services.Gemini;
using Microsoft.AspNetCore.SignalR;

namespace Brendha.Spike.Server.Workers
{
    public class AudioProcessorWorker(ILogger<AudioProcessorWorker> logger,
                                      AudioStreamBuffer buffer,
                                      InterviewSessionManager sessionManager) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var packet in buffer.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var orchestrator = sessionManager.GetSession(packet.SessionId);
                    if (orchestrator != null && orchestrator.IsConnected)
                    {
                        await orchestrator.SendAudioChunkAsync(packet.Payload);
                    }
                    else
                    {
                        logger.LogDebug($"Session not found or disconnected for sessionId {packet.SessionId}, skipping audio packet");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error sending audio chunk");
                }
            }
        }
    }
}
