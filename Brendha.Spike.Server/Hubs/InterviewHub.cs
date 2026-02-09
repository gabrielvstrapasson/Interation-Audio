using Brendha.Spike.Core.Models;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Brendha.Spike.Server.Hubs
{
    public class InterviewHub(AudioStreamBuffer buffer,
        ILogger<InterviewHub> logger,
        GeminiLiveService geminiService) : Hub
    {

        public async Task StartInterview(string resumeJson)
        {
            logger.LogInformation($"[Hub] Recebido comando StartInterview. Payload presente: {!string.IsNullOrEmpty(resumeJson)}");
            await geminiService.ConnectAsync(resumeJson);
        }
        public async Task SendAudioChunk(string base64data)
        {
            try
            {
                if (string.IsNullOrEmpty(base64data))
                {
                    logger.LogWarning("Received null audio from connection {ConnectionId}", Context.ConnectionId);
                    return;
                }

                byte[] data = Convert.FromBase64String(base64data);
                if (buffer == null) return;


                var packet = new AudioPacket
                {
                    Payload = data,
                    SessionId = Context.ConnectionId,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                await buffer.WriteAsync(packet);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Error for processing audio: {ex.Message}");
                throw;
            }


        }
        public async Task SendUserText(string text) 
        {
            await geminiService.AppendUserHistory(text);
        }

        public async Task RequestSessionTimer()
        {
            var seconds = geminiService.GetTimeRemaining();
            await Clients.Caller.SendAsync("SessionStarted", seconds);
        }
    }
}
