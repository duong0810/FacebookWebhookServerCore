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
                    System.Diagnostics.Debug.WriteLine($"Payload: {body}"); // Log payload để kiểm tra

                    var json = JObject.Parse(body);
                    var entries = json["entry"];

                    if (entries != null)
                    {
                        using (var db = new AppDbContext())
                        {
                            foreach (var entry in entries)
                            {
                                var messaging = entry["messaging"];
                                if (messaging != null)
                                {
                                    foreach (var messageEvent in messaging)
                                    {
                                        var senderId = messageEvent["sender"]?["id"]?.ToString();
                                        var messageText = messageEvent["message"]?["text"]?.ToString();

                                        if (!string.IsNullOrEmpty(senderId) && !string.IsNullOrEmpty(messageText))
                                        {
                                            // Lưu tin nhắn vào cơ sở dữ liệu
                                            db.Messages.Add(new Message
                                            {
                                                SenderId = senderId,
                                                Content = messageText,
                                                Time = DateTime.Now
                                            });
                                            await db.SaveChangesAsync();

                                            // Log thông tin tin nhắn
                                            System.Diagnostics.Debug.WriteLine($"Sender ID: {senderId}, Message: {messageText}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                return Ok(); // Trả về 200 OK
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