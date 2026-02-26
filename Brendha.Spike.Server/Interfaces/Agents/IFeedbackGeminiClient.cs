namespace Brendha.Spike.Server.Interfaces.Agents
{
    public interface IFeedbackGeminiClient 
    {
        Task<string> GenerateAsync(string prompt);
    }
}
