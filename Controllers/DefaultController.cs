using Microsoft.AspNetCore.Mvc;

namespace FacebookWebhookServerCore.Controllers
{
    public class DefaultController : Controller
    {
        [Route("/")]
        [HttpGet]
        public IActionResult Index()
        {
            return Content("<h1>Server is running</h1>", "text/html");
        }

        [Route("/zalo_verifierQE2wTxFh3bDmdBm9-hbk1YYismB4sW1TD3a.html")]
        [HttpGet]
        public ContentResult ZaloDomainVerification()
        {
            return Content("QE2wTxFh3bDmdBm9-hbk1YYismB4sW1TD3a", "text/plain");
        }
    }
}