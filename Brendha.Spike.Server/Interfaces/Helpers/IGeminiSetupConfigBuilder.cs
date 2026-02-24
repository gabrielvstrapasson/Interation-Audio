using Brendha.Spike.Core.Models;
using Brendha.Spike.Core.Models.DTOs.Gemini;

namespace Brendha.Spike.Server.Interfaces.Helpers
{
    public interface IGeminiSetupConfigBuilder
    {
        GeminiSetupRequest Build(JobPosition job, string resumeText, string? reconnectionContext = null);
    }
}
