using Brendha.Spike.Core.Models;
using Brendha.Spike.Core.Models.DTOs.Agents;

namespace Brendha.Spike.Server.Interfaces.Agents
{
    public interface IGeminiFeedbackAgent
    {
        Task<FeedbackResultDto> GenerateFeedbackAsync(string logPath, JobPosition jobPosition);
    }
}
