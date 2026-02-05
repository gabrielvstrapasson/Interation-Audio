using Brendha.Spike.Server.Services.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Brendha.Spike.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobContoller : ControllerBase
    {
        [HttpGet]
        public IActionResult GetCurrentJob()
        {
            var job = JobMockService.GetCurrentJob();
            return Ok(job);
        }
    }
}
