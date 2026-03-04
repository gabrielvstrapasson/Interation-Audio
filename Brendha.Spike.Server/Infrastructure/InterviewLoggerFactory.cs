using Brendha.Spike.Server.Interfaces.Logger;

namespace Brendha.Spike.Server.Infrastructure
{
    public class InterviewLoggerFactory (ILoggerFactory loggerFactory): IInterviewLoggerFactory
    {
        private readonly ILoggerFactory _loggerFactory = loggerFactory;
        public IInterviewFileLogger Create(string connectionId)
        {
            var logger = _loggerFactory.CreateLogger<InterviewFileLogger>();
            return new InterviewFileLogger(logger, connectionId);
        }
    }
}
