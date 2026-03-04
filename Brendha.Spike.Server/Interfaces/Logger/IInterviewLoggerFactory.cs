namespace Brendha.Spike.Server.Interfaces.Logger
{
    public interface IInterviewLoggerFactory
    {
        IInterviewFileLogger Create(string connectionId);
    }
}
