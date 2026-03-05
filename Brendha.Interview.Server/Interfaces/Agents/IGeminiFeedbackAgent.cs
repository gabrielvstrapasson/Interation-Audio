using Brendha.Interview.Core.Models;
using Brendha.Interview.Core.Models.DTOs.Agents;

namespace Brendha.Interview.Server.Interfaces.Agents
{
    public interface IGeminiFeedbackAgent
    {
        Task<FeedbackResultDto> GenerateFeedbackAsync(string logPath, JobPosition jobPosition);
    }
}
