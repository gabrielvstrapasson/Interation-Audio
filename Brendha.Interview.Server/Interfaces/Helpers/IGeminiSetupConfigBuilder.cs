using Brendha.Interview.Core.Models;
using Brendha.Interview.Core.Models.DTOs.Gemini;

namespace Brendha.Interview.Server.Interfaces.Helpers
{
    public interface IGeminiSetupConfigBuilder
    {
        GeminiSetupRequest Build(JobPosition job, string resumeText, string? reconnectionContext = null);
    }
}
