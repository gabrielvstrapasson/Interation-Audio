namespace Brendha.Spike.Server.Interfaces.Logger
{
    public interface IInterviewLogger
    {
        string LogFilePath { get; }
        Task LogAsync(string role, string message);
        string GetHistory();
    }
}
