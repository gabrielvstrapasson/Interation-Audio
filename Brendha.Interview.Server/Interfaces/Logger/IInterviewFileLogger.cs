namespace Brendha.Interview.Server.Interfaces.Logger
{
    public interface IInterviewFileLogger
    {
        string LogFilePath { get; }
        Task LogAsync(string role, string message);
        void FinalizeAndReset();
        string GetHistory();
    }
}
