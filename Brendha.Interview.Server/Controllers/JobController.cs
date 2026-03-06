using Brendha.Interview.Core.Models;
using Brendha.Interview.Server.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Brendha.Interview.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobController(ILogger<JobController> logger, IJobService jobService) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetCurrentJob()
        {
            var job = await jobService.GetCurrentJobAsync();
            return Ok(job);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateJob([FromBody] JobPosition job)
        {
            try
            {
                await jobService.UpdateJobAsync(job);
                var updated = await jobService.GetCurrentJobAsync();
                return Ok(new { message = "Job position updated successfully", job = updated });
            }
            catch (ValidationException vex)
            {
               return BadRequest(vex.Message);
            }
        }

        [HttpPost("reset")]
        public async Task<IActionResult> ResetToDefault()
        {
            await jobService.ResetToDefaultAsync();
            var job = await jobService.GetCurrentJobAsync();
            return Ok(new { message = "Job position reset to default", job });
        }

    }
}
