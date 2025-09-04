using Microsoft.AspNetCore.Mvc;

namespace Webhook_Message.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            // Tạm thời chỉ trả 200 OK để test deploy
            return Ok("Webhook is running");
        }

        [HttpPost]
        public IActionResult Post()
        {
            // Tạm thời chỉ nhận request để test
            return Ok();
        }
    }
}