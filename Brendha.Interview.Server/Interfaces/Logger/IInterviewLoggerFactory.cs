namespace Brendha.Interview.Server.Interfaces.Logger
{
    public interface IInterviewLoggerFactory
    {
        IInterviewFileLogger Create(string connectionId);
    }
}
