using Brendha.Interview.Core.Models;

namespace Brendha.Interview.Server.Interfaces.Repositories.Job
{
    public interface IJobRepository
    {
        Task<JobPosition?> GetCurrentJobAsync();
        Task UpdateJobAsync(JobPosition job);
        Task ResetToDefaultAsync();
    }
}
