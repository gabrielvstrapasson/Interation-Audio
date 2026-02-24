namespace Brendha.Spike.Server.Interfaces.Logger
{
    public interface IInterviewLogger
    {
        Task LogAsync(string role, string message);
        string GetHistory();
    }
}
