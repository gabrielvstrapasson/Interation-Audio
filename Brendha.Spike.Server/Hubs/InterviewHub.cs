using Brendha.Spike.Core.Models;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Services.Gemini;
using Microsoft.AspNetCore.SignalR;

namespace Brendha.Spike.Server.Hubs
{
    public class InterviewHub(AudioStreamBuffer buffer,
        ILogger<InterviewHub> logger,
        GeminiLiveService geminiService) : Hub
    {

        public async Task StartInterview()
        {
            logger.LogInformation($"StartInterview called from connectionId: {Context.ConnectionId}");

            if (!geminiService.HasResume)
            {
                logger.LogWarning("Attempt to start interview without reusme");
                await Clients.Caller.SendAsync("ResumeRequired", "Please upload your resume before starting the interview.");
                return;
            }
            try
            {
            await geminiService.ConnectAsync();
            logger.LogInformation("Interview started successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error starting interview");
                await Clients.Caller.SendAsync("Error", $"Failed to start interview: {ex.Message}");
            }
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
            await geminiService.LogSystemMessage(text);
        }

        public async Task RequestSessionTimer()
        {
            var seconds = geminiService.GetTimeRemaining();
            await Clients.Caller.SendAsync("UpdateTimer", seconds);
        }
        public async Task UploadResume(string resumeJson)
        {
            if (string.IsNullOrEmpty(resumeJson))
            {
                await Clients.Caller.SendAsync("ResumeUploadFailed", "Resume data is empty.");
                return;
            }

            try
            {
                await geminiService.SetResumeAsync(resumeJson);
                logger.LogInformation("Resume send with successful in active session");
            }
            catch (Exception ex)
            {
                logger.LogError($"Error to updated resume: {ex.Message}");
                await Clients.Caller.SendAsync("ResumeUploadFailed", "Failed to upload resume:", ex.Message);
            }
        }
        public async Task SubmitResume(string resumeJson)
        {
            await geminiService.SetResumeAsync(resumeJson);
        }
        public async Task EndInterview()
        {
            await geminiService.DisconnectAsync();
        }
    }
}
