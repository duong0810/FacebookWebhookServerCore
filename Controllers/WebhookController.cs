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
            System.Diagnostics.Debug.WriteLine("Post method started."); // Log bắt đầu phương thức

            try
            {
                if (Request.Body == null)
                {
                    System.Diagnostics.Debug.WriteLine("Request body is null.");
                    return BadRequest("Request body is null.");
                }

                using (var reader = new StreamReader(Request.Body))
                {
                    var body = await reader.ReadToEndAsync();
                    System.Diagnostics.Debug.WriteLine($"Payload: {body}"); // Log payload

                    if (string.IsNullOrEmpty(body))
                    {
                        System.Diagnostics.Debug.WriteLine("Request body is empty.");
                        return BadRequest("Invalid JSON payload.");
                    }

                    JObject json;
                    try
                    {
                        json = JObject.Parse(body);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Invalid JSON: {ex.Message}");
                        return BadRequest("Invalid JSON format.");
                    }

                    var entries = json["entry"];
                    if (entries == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Entries not found in JSON.");
                        return BadRequest("Invalid JSON structure.");
                    }

                    using (var db = new AppDbContext())
                    {
                        foreach (var entry in entries)
                        {
                            var messaging = entry["messaging"];
                            if (messaging == null)
                            {
                                System.Diagnostics.Debug.WriteLine("Messaging not found in entry.");
                                continue;
                            }

                            foreach (var messageEvent in messaging)
                            {
                                var senderId = messageEvent["sender"]?["id"]?.ToString() ?? string.Empty;
                                var messageText = messageEvent["message"]?["text"]?.ToString() ?? string.Empty;

                                if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(messageText))
                                {
                                    System.Diagnostics.Debug.WriteLine("SenderId or MessageText is null or empty.");
                                    continue;
                                }

                                System.Diagnostics.Debug.WriteLine($"Sender ID: {senderId}, Message: {messageText}");

                                db.Messages.Add(new Message
                                {
                                    SenderId = senderId,
                                    Content = messageText,
                                    Time = DateTime.Now
                                });
                                await db.SaveChangesAsync();
                            }
                        }
                    }
                }
                System.Diagnostics.Debug.WriteLine("Post method completed successfully.");
                return Ok();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, "Internal Server Error");
            }
        }
    }
}