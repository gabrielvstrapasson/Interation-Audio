using Brendha.Spike.Core.Models;
using Brendha.Spike.Server.Interfaces.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Brendha.Spike.Server.Services.Gemini.Agents
{
    public class GeminiFeedbackAgent : IGeminiFeedbackAgent
    {
        public Task<string> GenerateFeedbackAsync(string logPath, JobPosition jobPosition)
        {
            throw new NotImplementedException();
        }
    }
}
