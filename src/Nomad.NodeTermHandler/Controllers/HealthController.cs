using Microsoft.AspNetCore.Mvc;
using Nomad.NodeTermHandler.Models;

namespace Nomad.NodeTermHandler.Controllers
{
    [Route("")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ApiController]
    public class HealthController : ControllerBase
    {
        private readonly Status _status;

        private bool readiness => !liveness && !startup;
        private bool liveness => Request.Query.ContainsKey("liveness");
        private bool startup => Request.Query.ContainsKey("startup");

        public HealthController(Status status)
        {
            _status = status;
        }

        [Route("healthz")]
        [HttpGet]
        public ObjectResult Get()
        {
            HttpContext.Items.Add("Health", "Page"); // Request log at debug level
            return readiness && _status.ShuttingDown ? Problem("Shutting Down", statusCode: 418) : Ok("Success");
        }
    }
}
