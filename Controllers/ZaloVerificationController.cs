using Microsoft.AspNetCore.Mvc;

namespace FacebookWebhookServerCore.Controllers
{
    public class ZaloVerificationController : Controller
    {
        [Route("zalo_verifierQE2wTxFh3bDmdBm9-hbk1YYismB4sW1TD3a.html")]
        [HttpGet]
        public ContentResult ZaloDomainVerification()
        {
            // Trả về chính xác mã xác thực không có HTML tags
            return Content("QE2wTxFh3bDmdBm9-hbk1YYismB4sW1TD3a", "text/plain");
        }
    }
}