using Brendha.Interview.Core.Models;

namespace Brendha.Interview.Server.Interfaces.Services
{
    public interface IJobService
    {
        Task<JobPosition?> GetCurrentJobAsync();
        Task UpdateJobAsync(JobPosition job);
        Task ResetToDefaultAsync();
    }
}
