using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                var parsedBody = JsonSerializer.Deserialize<object>(body); // Parse raw
                var response = new
                {
                    status = "success",
                    receivedBody = parsedBody
                };
                // Serialize toàn bộ response với định dạng đẹp
                return new ContentResult
                {
                    Content = JsonSerializer.Serialize(response, jsonOptions),
                    ContentType = "application/json",
                    StatusCode = 200
                };
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
                var pageAccessToken = "EAASBBC1s6fgBP..."; // Thay bằng Page Access Token của bạn
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

        public class MessageRequest
        {
            public string RecipientId { get; set; }
            public string Message { get; set; }
        }
    }
}