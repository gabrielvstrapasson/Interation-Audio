using Brendha.Spike.Core.Models;
using Brendha.Spike.Server.Services.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography.X509Certificates;

namespace Brendha.Spike.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobController(ILogger<JobController> logger) : ControllerBase
    {
        [HttpGet]
        public IActionResult GetCurrentJob()
        {
            var job = JobMockService.GetCurrentJob();
            return Ok(job);
        }

        [HttpPut]
        public IActionResult UpdateJob([FromBody] JobPosition job)
        {
            if (job == null) return BadRequest("Job data is required.");
            if (string.IsNullOrWhiteSpace(job.Title)) return BadRequest("Job title is required.");
            if (string.IsNullOrWhiteSpace(job.Level)) return BadRequest("Job level is required.");
            try
            {
                JobMockService.UpdateJob(job);
                return Ok(new
                {
                    message = "Job position updated successfully",
                    job = JobMockService.GetCurrentJob()
                });
            }
            catch (Exception ex)
            {
                logger.LogError($"Error updating job: {ex.Message}");
                return StatusCode(500, "Error updating job position.");
            }
        }

        [HttpPost("reset")]
        public IActionResult ResetToDefault()
        {
            try
            {
                JobMockService.ResetToDefault();
                logger.LogInformation("Job reset to default");

                return Ok(new
                {
                    message = "Job position reset to default",
                    job = JobMockService.GetCurrentJob()
                });
            }
            catch (Exception ex)
            {
                logger.LogError($"Error resetting job: {ex.Message}");
                return StatusCode(500, "Error resetting job position.");
            }
        }

    }
}
