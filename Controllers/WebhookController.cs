using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;

namespace FacebookWebhookServerCore.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly string _verifyToken = "kosmosdevelopment"; // Thay bằng mã xác minh bạn nhập trên Meta

        [HttpGet]
        public IActionResult Get()
        {
            // Lấy tham số từ query string
            string verifyToken = Request.Query["hub.verify_token"];
            string challenge = Request.Query["hub.challenge"];
            string mode = Request.Query["hub.mode"];

            // Kiểm tra mode và token
            if (mode == "subscribe" && verifyToken == _verifyToken)
            {
                // Trả về challenge để xác thực (plain text, không JSON)
                return Content(challenge, "text/plain");
            }

            // Nếu không khớp, trả lỗi
            return BadRequest("Verify token mismatch");
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            // Đọc body request từ Facebook (JSON tin nhắn)
            using (var reader = new StreamReader(Request.Body))
            {
                var body = await reader.ReadToEndAsync();
                // Tạm thời log (sau này lưu database hoặc gửi đến Windows Forms)
                System.Diagnostics.Debug.WriteLine("Received: " + body);
            }

            // Trả 200 OK để Facebook biết đã nhận
            return Ok();
        }
    }
}