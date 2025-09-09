using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Webhook_Message.Data;
using Webhook_Message.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace FacebookWebhookServerCore.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly ILogger<WebhookController> _logger;
        private readonly string _verifyToken = "kosmosdevelopment";

        public WebhookController(ILogger<WebhookController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Get()
        {
            string verifyToken = Request.Query["hub.verify_token"];
            string challenge = Request.Query["hub.challenge"];
            string mode = Request.Query["hub.mode"];

            if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(verifyToken) || string.IsNullOrEmpty(challenge))
            {
                _logger.LogWarning("Missing required parameters in GET request");
                return BadRequest("Missing required parameters");
            }

            if (mode == "subscribe" && verifyToken == _verifyToken)
            {
                _logger.LogInformation("Webhook verified successfully with challenge: {Challenge}", challenge);
                return Content(challenge, "text/plain");
            }
            _logger.LogWarning("Verify token mismatch: {VerifyToken}", verifyToken);
            return BadRequest("Verify token mismatch");
        }

        [HttpGet("messages")]
        public async Task<IActionResult> GetMessages([FromServices] AppDbContext dbContext)
        {
            var messages = await dbContext.Messages.OrderByDescending(m => m.Time).ToListAsync();
            var messageViewModels = messages.Select(m => new MessageViewModel
            {
                Id = m.Id,
                SenderId = m.SenderId,
                RecipientId = m.RecipientId,
                Content = m.Content,
                Time = m.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                Direction = m.Direction
            });
            return Ok(messageViewModels);
        }

        [HttpGet("messages/by-customer/{customerId}")]
        public async Task<IActionResult> GetMessagesByCustomer(
        [FromServices] AppDbContext dbContext, string customerId)
        {
            var messages = await dbContext.Messages
                .Where(m => m.SenderId == customerId || m.RecipientId == customerId)
                .OrderByDescending(m => m.Time)
                .ToListAsync();
            var messageViewModels = messages.Select(m => new MessageViewModel
            {
                Id = m.Id,
                SenderId = m.SenderId,
                RecipientId = m.RecipientId,
                Content = m.Content,
                Time = m.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                Direction = m.Direction
            });
            return Ok(messageViewModels);
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            try
            {
                string body;
                Request.EnableBuffering();
                using (var reader = new StreamReader(Request.Body, encoding: System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    body = await reader.ReadToEndAsync();
                }

                _logger.LogInformation("Received raw body: {Body}", body);

                if (string.IsNullOrEmpty(body))
                {
                    _logger.LogWarning("Received empty body");
                    return Ok(new { status = "success", receivedBody = "No data" });
                }

                // Parse Facebook webhook payload
                using var doc = JsonDocument.Parse(body);
                var entry = doc.RootElement.GetProperty("entry")[0];
                var messaging = entry.GetProperty("messaging")[0];

                var senderId = messaging.GetProperty("sender").GetProperty("id").GetString();
                var recipientId = messaging.GetProperty("recipient").GetProperty("id").GetString();
                var content = messaging.GetProperty("message").GetProperty("text").GetString();
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(
                    messaging.GetProperty("timestamp").GetInt64()
                ).UtcDateTime;

                // Lưu vào database
                var dbContext = HttpContext.RequestServices.GetService(typeof(Webhook_Message.Data.AppDbContext)) as Webhook_Message.Data.AppDbContext;
                var message = new Message
                {
                    SenderId = senderId ?? "",
                    RecipientId = recipientId ?? "",
                    Content = content ?? "",
                    Time = timestamp,
                    Direction = "inbound"
                };
                dbContext.Messages.Add(message);
                await dbContext.SaveChangesAsync();

                return Ok(new { status = "success" });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON Parse Error");
                return StatusCode(400, new { error = "Invalid JSON format" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return StatusCode(500, new { error = ex.Message });
            }
        }
        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromBody] MessageRequest request)
        {
            try
            {
                var pageAccessToken = "EAASBBCls6fgBPYafEJZA2pWrDBvSy4VlkeVLpg9BFQJwZCB3fuOZBRJu4950XhFnNPkwgkfDvqKY17X52Kgtpl5ZA68UqFfmXbWSrU7xnHxZCShxzM39ZBqZBxmJGLVKNs1SrqpDs9Y9J0L3RW3TWcZAUyIIXZAZAWZCFBv4ywgekXYyUSkA2qaSIhwDvj88qQ8QWdNEZA7oUx78gT6cWUmWhhMHIe0P";
                var url = $"https://graph.facebook.com/v21.0/me/messages?access_token={pageAccessToken}";

                var payload = new
                {
                    recipient = new { id = request.RecipientId },
                    message = new { text = request.Message }
                };

                using var httpClient = new HttpClient();
                var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    // Lưu outbound message
                    var dbContext = HttpContext.RequestServices.GetService(typeof(Webhook_Message.Data.AppDbContext)) as Webhook_Message.Data.AppDbContext;
                    var message = new Message
                    {
                        SenderId = "807147519144166", // Thay bằng page id thực tế nếu cần
                        RecipientId = request.RecipientId,
                        Content = request.Message,
                        Time = DateTime.UtcNow,
                        Direction = "outbound"
                    };
                    dbContext.Messages.Add(message);
                    await dbContext.SaveChangesAsync();

                    var responseContent = await response.Content.ReadAsStringAsync();
                    return Ok(new { status = "success", details = responseContent });
                }

                var errorResponse = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, new { status = "error", details = errorResponse });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message");
                return StatusCode(500, new { error = ex.Message });
            }
        }

    }
}