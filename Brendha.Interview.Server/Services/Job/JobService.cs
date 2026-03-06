using Brendha.Interview.Core.Models;
using Brendha.Interview.Server.Interfaces.Repositories.Job;
using Brendha.Interview.Server.Interfaces.Services;
using System.ComponentModel.DataAnnotations;

namespace Brendha.Interview.Server.Services.Job
{
    public class JobService (IJobRepository jobRepository): IJobService
    {
        private readonly IJobRepository _jobRepository = jobRepository; 
        public Task<JobPosition?> GetCurrentJobAsync() => _jobRepository.GetCurrentJobAsync();

        public Task ResetToDefaultAsync() => _jobRepository.ResetToDefaultAsync();

        public async Task UpdateJobAsync(JobPosition job)
        {
            if (job == null) throw new ValidationException("Job data is required.");
            if (string.IsNullOrWhiteSpace(job.Title)) throw new ValidationException("Job title is required.");
            if (string.IsNullOrWhiteSpace(job.Level)) throw new ValidationException("Job level is required.");

            await _jobRepository.UpdateJobAsync(job);
        }
    }
}
