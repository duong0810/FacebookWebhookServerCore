using Microsoft.AspNetCore.Mvc;

namespace Webhook_Message.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get([FromQuery] string hub_mode, [FromQuery] string hub_challenge, [FromQuery] string hub_verify_token)
        {
            // Xác minh token do Facebook gửi
            const string verifyToken = "kosmosdevelopment"; // Thay bằng token bạn đã cấu hình trên Facebook Developer
            if (hub_mode == "subscribe" && hub_verify_token == verifyToken)
            {
                return Ok(hub_challenge); // Trả về mã xác minh
            }
            return Unauthorized();
        }

        [HttpPost]
        public IActionResult Post([FromBody] object payload)
        {
            // Xử lý payload từ Facebook
            return Ok();
        }
    }
}