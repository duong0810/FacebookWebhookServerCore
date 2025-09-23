using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Webhook_Message.Data;
using Webhook_Message.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using FacebookWebhookServerCore.Hubs;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using System.Net.Http;

namespace FacebookWebhookServerCore.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly ILogger<WebhookController> _logger;
        private readonly string _verifyToken = "kosmosdevelopment";
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly Cloudinary _cloudinary;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _pageAccessToken = "EAASBBCls6fgBPYafEJZA2pWrDBvSy4VlkeVLpg9BFQJwZCB3fuOZBRJu4950XhFnNPkwgkfDvqKY17X52Kgtpl5ZA68UqFfmXbWSrU7xnHxZCShxzM39ZBqZBxmJGLVKNs1SrqpDs9Y9J0L3RW3TWcZAUyIIXZAZAWZCFBv4ywgekXYyUSkA2qaSIhwDvj88qQ8QWdNEZA7oUx78gT6cWUmWhhMHIe0P"; // Thay bằng Page Access Token của bạn

        public WebhookController(ILogger<WebhookController> logger, IHubContext<ChatHub> hubContext, Cloudinary cloudinary, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _hubContext = hubContext;
            _cloudinary = cloudinary;
            _httpClientFactory = httpClientFactory;
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
            var messages = await dbContext.Messages
                .OrderByDescending(m => m.Time)
                .Include(m => m.Sender)
                .Select(m => new MessageViewModel
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    RecipientId = m.RecipientId,
                    Content = m.Content,
                    Time = m.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = m.Direction,
                    SenderName = m.Sender.Name,
                    SenderAvatar = m.Sender.AvatarUrl
                })
                .ToListAsync();
            return Ok(messages);
        }

        [HttpGet("messages/by-customer/{customerId}")]
        public async Task<IActionResult> GetMessagesByCustomer(
        [FromServices] AppDbContext dbContext,
        string customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        {
            int skip = (page - 1) * pageSize;

            var messages = await dbContext.Messages
                .Where(m => m.SenderId == customerId || m.RecipientId == customerId)
                .OrderByDescending(m => m.Time)
                .Skip(skip)
                .Take(pageSize)
                .Include(m => m.Sender)
                .Select(m => new MessageViewModel
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    RecipientId = m.RecipientId,
                    Content = m.Content,
                    Time = m.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = m.Direction,
                    SenderName = m.Sender.Name,
                    SenderAvatar = m.Sender.AvatarUrl
                })
                .ToListAsync();

            return Ok(messages);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromServices] AppDbContext dbContext)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(Request.Body))
                {
                    body = await reader.ReadToEndAsync();
                }

                _logger.LogInformation("Received raw body: {Body}", body);

                using var doc = JsonDocument.Parse(body);
                var entry = doc.RootElement.GetProperty("entry")[0];
                var messaging = entry.GetProperty("messaging")[0];

                var senderId = messaging.GetProperty("sender").GetProperty("id").GetString();
                var recipientId = messaging.GetProperty("recipient").GetProperty("id").GetString();
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(messaging.GetProperty("timestamp").GetInt64()).UtcDateTime;

                var messageElement = messaging.GetProperty("message");
                string content;

                if (messageElement.TryGetProperty("text", out var textElement))
                {
                    content = textElement.GetString();
                }
                else if (messageElement.TryGetProperty("attachments", out var attachmentsElement))
                {
                    var firstAttachment = attachmentsElement[0];
                    content = firstAttachment.GetProperty("payload").GetProperty("url").GetString();
                }
                else
                {
                    content = "[Unsupported message type]";
                }

                var customer = await GetOrCreateCustomerAsync(dbContext, senderId);

                var message = new Message
                {
                    SenderId = customer.FacebookId,
                    RecipientId = recipientId,
                    Content = content,
                    Time = timestamp,
                    Direction = "inbound"
                };
                dbContext.Messages.Add(message);
                await dbContext.SaveChangesAsync();

                var messageViewModel = new MessageViewModel
                {
                    Id = message.Id,
                    SenderId = message.SenderId,
                    RecipientId = message.RecipientId,
                    Content = message.Content,
                    Time = message.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = message.Direction,
                    SenderName = customer.Name,
                    SenderAvatar = customer.AvatarUrl
                };
                await _hubContext.Clients.All.SendAsync("ReceiveMessage", messageViewModel);

                return Ok(new { status = "success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromServices] AppDbContext dbContext, [FromBody] MessageRequest request)
        {
            try
            {
                var url = $"https://graph.facebook.com/v21.0/me/messages?access_token={_pageAccessToken}";
                var payload = new { recipient = new { id = request.RecipientId }, message = new { text = request.Message } };

                using var httpClient = _httpClientFactory.CreateClient();
                var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    // Đảm bảo Page tồn tại trong bảng Customer
                    var pageId = "807147519144166";
                    var pageAsCustomer = await dbContext.Customers.FindAsync(pageId);
                    if (pageAsCustomer == null)
                    {
                        pageAsCustomer = new Customer
                        {
                            FacebookId = pageId,
                            Name = "My Page", // Hoặc tên Page của bạn
                            AvatarUrl = "", // Thêm URL avatar của Page nếu có
                            LastUpdated = DateTime.UtcNow
                        };
                        dbContext.Customers.Add(pageAsCustomer);
                    }

                    var message = new Message
                    {
                        SenderId = pageId, // Page ID
                        RecipientId = request.RecipientId,
                        Content = request.Message,
                        Time = DateTime.UtcNow,
                        Direction = "outbound"
                    };
                    dbContext.Messages.Add(message);
                    await dbContext.SaveChangesAsync();

                    var messageViewModel = new MessageViewModel
                    {
                        Id = message.Id,
                        SenderId = message.SenderId,
                        RecipientId = message.RecipientId,
                        Content = message.Content,
                        Time = message.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                        Direction = message.Direction,
                        // Lấy thông tin người gửi (là Page) để hiển thị trên UI
                        SenderName = pageAsCustomer.Name,
                        SenderAvatar = pageAsCustomer.AvatarUrl
                    };
                    await _hubContext.Clients.All.SendAsync("ReceiveMessage", messageViewModel);

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

        [HttpPost("send-attachment")]
        public async Task<IActionResult> SendAttachment([FromServices] AppDbContext dbContext, [FromForm] string recipientId, [FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("File is empty.");

            try
            {
                var fileUrl = await UploadToCloudinaryAsync(file);
                var attachmentType = GetAttachmentType(file.ContentType);
                var url = $"https://graph.facebook.com/v21.0/me/messages?access_token={_pageAccessToken}";

                var payload = new
                {
                    recipient = new { id = recipientId },
                    message = new { attachment = new { type = attachmentType, payload = new { url = fileUrl, is_reusable = true } } }
                };

                using var httpClient = _httpClientFactory.CreateClient();
                var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    // SỬA LỖI: Thêm logic kiểm tra và tạo Customer cho Page
                    var pageId = "807147519144166";
                    var pageAsCustomer = await dbContext.Customers.FindAsync(pageId);
                    if (pageAsCustomer == null)
                    {
                        pageAsCustomer = new Customer
                        {
                            FacebookId = pageId,
                            Name = "My Page", // Hoặc tên Page của bạn
                            AvatarUrl = "", // Thêm URL avatar của Page nếu có
                            LastUpdated = DateTime.UtcNow
                        };
                        dbContext.Customers.Add(pageAsCustomer);
                    }

                    var message = new Message
                    {
                        SenderId = pageId, // Page ID
                        RecipientId = recipientId,
                        Content = fileUrl,
                        Time = DateTime.UtcNow,
                        Direction = "outbound"
                    };
                    dbContext.Messages.Add(message);
                    await dbContext.SaveChangesAsync();

                    var messageViewModel = new MessageViewModel
                    {
                        Id = message.Id,
                        SenderId = message.SenderId,
                        RecipientId = message.RecipientId,
                        Content = message.Content,
                        Time = message.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                        Direction = message.Direction,
                        SenderName = pageAsCustomer.Name,
                        SenderAvatar = pageAsCustomer.AvatarUrl
                    };
                    await _hubContext.Clients.All.SendAsync("ReceiveMessage", messageViewModel);

                    var responseContent = await response.Content.ReadAsStringAsync();
                    return Ok(new { status = "success", details = responseContent });
                }

                var errorResponse = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, new { status = "error", details = errorResponse });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending attachment");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<Customer> GetOrCreateCustomerAsync(AppDbContext dbContext, string senderId)
        {
            var customer = await dbContext.Customers.FindAsync(senderId);

            if (customer == null || customer.LastUpdated < DateTime.UtcNow.AddDays(-1))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var requestUrl = $"https://graph.facebook.com/v21.0/{senderId}?fields=name,profile_pic&access_token={_pageAccessToken}";
                    var response = await client.GetAsync(requestUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(content);
                        var root = doc.RootElement;

                        var name = root.GetProperty("name").GetString();

                        // SỬA LỖI: Lấy URL avatar trực tiếp từ 'profile_pic'
                        var avatarUrl = root.GetProperty("profile_pic").GetString();

                        if (customer == null)
                        {
                            customer = new Customer { FacebookId = senderId };
                            dbContext.Customers.Add(customer);
                        }

                        customer.Name = name;
                        customer.AvatarUrl = avatarUrl;
                        customer.LastUpdated = DateTime.UtcNow;

                        await dbContext.SaveChangesAsync();
                    }
                    else // Thêm log để biết lý do API thất bại
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError("Failed to fetch profile from Facebook. Status: {StatusCode}, Response: {Response}", response.StatusCode, errorContent);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch customer profile from Facebook.");
                    if (customer == null)
                    {
                        customer = new Customer { FacebookId = senderId, Name = $"Customer {senderId}", AvatarUrl = "" };
                        dbContext.Customers.Add(customer);
                        await dbContext.SaveChangesAsync();
                    }
                }
            }
            return customer;
        }

        private async Task<string> UploadToCloudinaryAsync(IFormFile file)
        {
            var uploadResult = new RawUploadResult();
            if (file.Length > 0)
            {
                using (var stream = file.OpenReadStream())
                {
                    var uploadParams = new RawUploadParams() { File = new FileDescription(file.FileName, stream) };
                    uploadResult = await _cloudinary.UploadAsync(uploadParams);
                }
            }
            if (uploadResult.Error != null) throw new Exception(uploadResult.Error.Message);
            return uploadResult.SecureUrl.AbsoluteUri;
        }

        private string GetAttachmentType(string contentType)
        {
            if (contentType.StartsWith("image/")) return "image";
            if (contentType.StartsWith("video/")) return "video";
            if (contentType.StartsWith("audio/")) return "audio";
            return "file";
        }
    }
}