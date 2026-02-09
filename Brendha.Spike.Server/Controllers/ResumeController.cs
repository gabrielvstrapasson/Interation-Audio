using Brendha.Spike.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Brendha.Spike.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResumeController(ResumeProcessingService service) : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

            using var stream = file.OpenReadStream();
            var rawText = await service.ProcessPdfAsync(stream, file.FileName);

            if (string.IsNullOrEmpty(rawText)) return BadRequest("Error processing the file.");
            
            return Ok(new {resumeText = rawText});
        }
    }
}
