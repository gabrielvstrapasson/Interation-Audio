using Brendha.Spike.Core.Models;
using Brendha.Spike.Core.Models.DTOs.Agents;
using Brendha.Spike.Server.Infrastructure;
using Brendha.Spike.Server.Interfaces.Connection;
using Brendha.Spike.Server.Services.Gemini;
using Microsoft.AspNetCore.SignalR;

namespace Brendha.Spike.Server.Hubs
{
    public class InterviewHub(
        AudioStreamBuffer buffer,
        ILogger<InterviewHub> logger,
        InterviewSessionManager sessionManager) : Hub
    {

        #region Client-To-Server
        public async Task StartInterview()
        {
            logger.LogInformation($"StartInterview called from connectionId: {Context.ConnectionId}");
            var orchestrator = sessionManager.GetSession(Context.ConnectionId);

            if (!orchestrator.HasResume)
            {
                logger.LogWarning("Attempt to start interview without resume");
                await Clients.Caller.SendAsync("ResumeRequired", "Please upload your resume before starting the interview.");
                return;
            }
            try
            {
                await orchestrator.ConnectAsync();
                logger.LogInformation($"Interview started successfully for {Context.ConnectionId}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error starting interview for {Context.ConnectionId}");
                await Clients.Caller.SendAsync("Error", $"Failed to start interview: {ex.Message}");
            }
        }
        public async Task SendAudioChunk(string base64data)
        {
            if (string.IsNullOrEmpty(base64data)) return;

            var packet = new AudioPacket
            {
                Payload = Convert.FromBase64String(base64data),
                SessionId = Context.ConnectionId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await buffer.WriteAsync(packet);
        }

        public async Task RequestSessionTimer()
        {
            var orchestrator = sessionManager.GetSession(Context.ConnectionId);
            if (orchestrator == null) return;

            var seconds = orchestrator.GetTimeRemaining();
            await Clients.Caller.SendAsync("UpdateTimer", seconds);
        }
        public async Task UploadResume(string resumeJson)
        {
            var orchestrator = sessionManager.GetSession(Context.ConnectionId);
            if (orchestrator == null) return;
            await orchestrator.SetResumeAsync(resumeJson);
        }

        public async Task EndInterview()
        {
            var orchestrator = sessionManager.GetSession(Context.ConnectionId);
            if (orchestrator == null) return;

            await orchestrator.DisconnectAsync();
        }
        #endregion

        public override async Task OnConnectedAsync()
        {
            await sessionManager.CreateSessionAsync(Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await sessionManager.RemoveSessionAsync(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

    }
}
