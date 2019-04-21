using Microsoft.AspNetCore.Mvc;

namespace ES.Kubernetes.Reflector.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet("Liveness")]
        public IActionResult Liveness()
        {
            return Ok();
        }

        [HttpGet("readiness")]
        public IActionResult Readiness()
        {
            return Ok();
        }
    }
}