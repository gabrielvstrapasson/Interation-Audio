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

                Console.WriteLine($"Atualizando vaga: {job.Title}");
                Console.WriteLine($"Benefícios recebidos: {string.Join(", ", job.Benefits ?? new string[0])}");
                Console.WriteLine($"Mandatory Skills recebidas: {string.Join(", ", job.MandatorySkills?? new string[0])}");

                JobMockService.UpdateJob(job);
                return Ok(new
                {
                    message = $"Job position updated successfully \n{job}",
                    job = JobMockService.GetCurrentJob(),
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
