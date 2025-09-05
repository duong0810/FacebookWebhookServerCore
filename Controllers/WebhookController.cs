using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Threading.Tasks;

namespace FacebookWebhookServerCore.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly string _verifyToken = "kosmosdevelopment"; // Thay bằng mã của bạn

        [HttpGet]
        public IActionResult Get()
        {
            string verifyToken = Request.Query["hub.verify_token"];
            string challenge = Request.Query["hub.challenge"];
            string mode = Request.Query["hub.mode"];

            if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(verifyToken) || string.IsNullOrEmpty(challenge))
            {
                return BadRequest("Missing required parameters");
            }

            if (mode == "subscribe" && verifyToken == _verifyToken)
            {
                return Content(challenge, "text/plain");
            }
            return BadRequest("Verify token mismatch (không khớp)");
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            try
            {
                // Đọc toàn bộ nội dung yêu cầu và lưu vào biến
                Request.EnableBuffering();
                string body;
                using (var reader = new StreamReader(Request.Body, encoding: System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                {
                    body = await reader.ReadToEndAsync();
                }
                Request.Body.Position = 0; // Reset stream để tránh lỗi nếu cần đọc lại

                System.Diagnostics.Debug.WriteLine($"Raw body: {body}");

                if (string.IsNullOrEmpty(body))
                {
                    System.Diagnostics.Debug.WriteLine("Body is empty");
                    return Ok(new { status = "success", receivedBody = "No data" });
                }

                // Phân tích JSON một cách an toàn
                var json = JObject.Parse(body);
                var entries = json["entry"] as JArray;

                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        var changes = entry["changes"] as JArray;
                        if (changes != null)
                        {
                            foreach (var change in changes)
                            {
                                var value = change["value"] as JObject;
                                var message = value?["message"]?["text"]?.ToString();
                                var senderId = value?["from"]?["id"]?.ToString();

                                if (!string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(senderId))
                                {
                                    System.Diagnostics.Debug.WriteLine($"Message from {senderId}: {message}");
                                }
                            }
                        }
                    }
                }

                return Ok(new { status = "success", receivedBody = body });
            }
            catch (JsonReaderException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON Parse Error: {ex.Message}");
                return StatusCode(400, new { error = "Invalid JSON format" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}