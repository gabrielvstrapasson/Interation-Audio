using Brendha.Spike.Server.Interfaces.Agents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Brendha.Spike.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FeedbackController (IGeminiFeedbackAgent geminiFeedbackAgent) : ControllerBase
    {

    }
}
