namespace Brendha.Spike.Server.Infrastructure.Interfaces
{
    public interface IInterviewLogger
    {
        Task LogAsync(string role, string message);
        string GetHistory();
    }
}
