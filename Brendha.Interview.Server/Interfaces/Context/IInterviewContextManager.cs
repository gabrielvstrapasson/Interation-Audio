namespace Brendha.Interview.Server.Interfaces.Context
{
    public interface IInterviewContextManager : IDisposable
    {
        Task<string> BuildReconnectionContextAsync(string fullHistory);
    }
}
