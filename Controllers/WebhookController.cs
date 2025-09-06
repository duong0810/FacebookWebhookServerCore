using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
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

                var json = JObject.Parse(body);
                var entries = json["entry"] as JArray;

                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        var messaging = entry["messaging"] as JArray;
                        if (messaging != null)
                        {
                            foreach (var messageObj in messaging)
                            {
                                var senderId = messageObj["sender"]?["id"]?.ToString();
                                var message = messageObj["message"] as JObject;
                                var msgText = message?["text"]?.ToString() ?? string.Empty;

                                if (!string.IsNullOrEmpty(msgText) && !string.IsNullOrEmpty(senderId))
                                {
                                    _logger.LogInformation("Message from {SenderId}: {MessageText}", senderId, msgText);
                                }
                                else
                                {
                                    _logger.LogWarning("Incomplete message data: SenderId={SenderId}, Message={Message}", senderId, msgText);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning("No messaging data in entry");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("No entry data in payload");
                }

                var response = new
                {
                    status = "success",
                    receivedBody = JToken.Parse(body).ToString(Formatting.Indented)
                };
                return new JsonResult(response) { SerializerSettings = new JsonSerializerSettings { Formatting = Formatting.Indented } };
            }
            catch (JsonReaderException ex)
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
    }
}