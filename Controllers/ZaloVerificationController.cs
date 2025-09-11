using Microsoft.AspNetCore.Mvc;

namespace FacebookWebhookServerCore.Controllers
{
    public class ZaloVerificationController : Controller
    {
        [HttpGet("zalo_verifierQE2wTxFh3bDmdBm9-hbk1YYismB4sW1TD3a.html")]
        public ContentResult ZaloDomainVerification()
        {
            return Content("QE2wTxFh3bDmdBm9-hbk1YYismB4sW1TD3a", "text/html");
        }
    }
}