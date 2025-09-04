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
        private readonly string _verifyToken = "kosmosdevelopment"; // Thay bằng mã của bạn

        [HttpGet]
        public IActionResult Get()
        {
            string verifyToken = Request.Query["hub.verify_token"];
            string challenge = Request.Query["hub.challenge"];
            string mode = Request.Query["hub.mode"];

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
                string body;
                using (var reader = new StreamReader(Request.Body))
                {
                    body = await reader.ReadToEndAsync();
                }
                System.Diagnostics.Debug.WriteLine($"Raw body: {body}");

                var json = JObject.Parse(body);
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
                                System.Diagnostics.Debug.WriteLine($"Message from {senderId}: {message}");
                            }
                        }
                    }
                }
                return Ok();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, "Internal Server Error");
            }
        }
    }
}