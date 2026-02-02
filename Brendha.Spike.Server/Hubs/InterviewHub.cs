using Brendha.Spike.Core.Models;
using Brendha.Spike.Server.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace Brendha.Spike.Server.Hubs
{
    public class InterviewHub(AudioStreamBuffer buffer,
        ILogger<InterviewHub> logger) : Hub
    {
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
    }
}
