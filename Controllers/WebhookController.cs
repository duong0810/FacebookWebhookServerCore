using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Threading.Tasks;

namespace FacebookWebhookServerCore.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly string _verifyToken = "kosmosdevelopment"; // Thay bằng mã xác minh của bạn

        [HttpGet]
        public IActionResult Get()
        {
            string verifyToken = Request.Query["hub.verify_token"];
            string challenge = Request.Query["hub.challenge"];
            string mode = Request.Query["hub.mode"];

            if (mode == "subscribe" && verifyToken == _verifyToken)
            {
                return Content(challenge, "text/plain"); // Trả challenge để xác thực
            }
            return BadRequest("Verify token mismatch lê minh đương");
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            try
            {
                // Đọc body request từ Facebook
                using (var reader = new StreamReader(Request.Body))
                {
                    var body = await reader.ReadToEndAsync();
                    var json = JObject.Parse(body);

                    // Lấy thông tin tin nhắn (cấu trúc điển hình từ Facebook)
                    var entries = json["entry"];
                    foreach (var entry in entries)
                    {
                        var changes = entry["changes"];
                        if (changes != null)
                        {
                            foreach (var change in changes)
                            {
                                var value = change["value"];
                                var message = value?["message"]?["text"]?.ToString();
                                var senderId = value?["from"]?["id"]?.ToString();

                                if (!string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(senderId))
                                {
                                    // Tạm thời log (sau này lưu database)
                                    System.Diagnostics.Debug.WriteLine($"Message from {senderId}: {message}");
                                }
                            }
                        }
                    }
                }
                return Ok(); // Trả 200 OK
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing message: {ex.Message}");
                return StatusCode(500, "Internal Server Error");
            }
        }
    }
}