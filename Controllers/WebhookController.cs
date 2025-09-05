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
        private readonly string _verifyToken = "kosmosdevelopment";

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
            return BadRequest("Verify token mismatch");
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            try
            {
                // Đọc body một lần và lưu vào biến
                string body;
                Request.EnableBuffering();
                using (var reader = new StreamReader(Request.Body, encoding: System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    body = await reader.ReadToEndAsync();
                }
                // Không cần đặt lại Position nếu chỉ đọc một lần

                System.Diagnostics.Debug.WriteLine($"Raw body: {body}");

                if (string.IsNullOrEmpty(body))
                {
                    System.Diagnostics.Debug.WriteLine("Body is empty");
                    return Ok(new { status = "success", receivedBody = "No data" });
                }

                // Phân tích JSON an toàn
                var json = JObject.Parse(body);
                var entries = json["entry"] as JArray;

                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        var messaging = entry["messaging"] as JArray; // Thay changes bằng messaging nếu dùng messaging
                        if (messaging != null)
                        {
                            foreach (var message in messaging)
                            {
                                var value = message as JObject;
                                var msgText = value?["message"]?["text"]?.ToString();
                                var senderId = value?["sender"]?["id"]?.ToString();

                                if (!string.IsNullOrEmpty(msgText) && !string.IsNullOrEmpty(senderId))
                                {
                                    System.Diagnostics.Debug.WriteLine($"Message from {senderId}: {msgText}");
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