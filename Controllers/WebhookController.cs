using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Threading.Tasks;
using Webhook_Message.Data;
using Webhook_Message.Models;

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
            return BadRequest("Verify token mismatch");
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            try
            {
                using (var reader = new StreamReader(Request.Body))
                {
                    var body = await reader.ReadToEndAsync();
                    System.Diagnostics.Debug.WriteLine($"Payload: {body}"); // Log payload
                    var json = JObject.Parse(body);
                    var entries = json["entry"];

                    using (var db = new AppDbContext())
                    {
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
                                        db.Messages.Add(new Message
                                        {
                                            SenderId = senderId,
                                            Content = message,
                                            Time = DateTime.Now
                                        });
                                        await db.SaveChangesAsync();
                                    }
                                }
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