using Brendha.Spike.Core.Models.DTOs.Agents;
using Brendha.Spike.Server.Interfaces.Agents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Brendha.Spike.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FeedbackController (IGeminiFeedbackAgent geminiFeedbackAgent) : ControllerBase
    {
        [HttpPost("generate")]
        public async Task<IActionResult> GenerateFeedback([FromBody] FeedbackRequestDto request)
        {
            var result = await geminiFeedbackAgent.GenerateFeedbackAsync(request.LogPath, request.JobPosition);
            return Ok(result);
        }
    }
}
