using Brendha.Spike.Core.Models;

namespace Brendha.Spike.Server.Interfaces.Agents
{
    public interface IGeminiFeedbackAgent
    {
        Task<string> GenerateFeedbackAsync(string logPath, JobPosition jobPosition);
    }
}
