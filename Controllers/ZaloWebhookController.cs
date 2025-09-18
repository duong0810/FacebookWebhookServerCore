using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using FacebookWebhookServerCore.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Webhook_Message.Data;
using Webhook_Message.Models;
using Webhook_Message.Services;

namespace FacebookWebhookServerCore.Controllers
{
    [ApiController]
    [Route("api/zalo-webhook")]
    public class ZaloWebhookController : ControllerBase
    {
        private readonly ILogger<ZaloWebhookController> _logger;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _oaId;
        private readonly string _oaSecret;
        private readonly ZaloAuthService _zaloAuthService;
        private readonly Cloudinary _cloudinary;

        public ZaloWebhookController(
            ILogger<ZaloWebhookController> logger,
            IHubContext<ChatHub> hubContext,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ZaloAuthService zaloAuthService,
            Cloudinary cloudinary)
        {
            _logger = logger;
            _hubContext = hubContext;
            _httpClientFactory = httpClientFactory;
            _oaId = configuration["ZaloOA:OaId"];
            _oaSecret = configuration["ZaloOA:OaSecret"];
            _zaloAuthService = zaloAuthService;
            _cloudinary = cloudinary;
        }

        [HttpGet]
        public IActionResult Get()
        {
            _logger.LogInformation("Received Zalo domain verification request");
            foreach (var param in Request.Query)
            {
                _logger.LogInformation("Query parameter: {Key} = {Value}", param.Key, param.Value);
            }
            return Ok("Zalo webhook verification successful");
        }

        [HttpGet("messages")]
        public async Task<IActionResult> GetMessages([FromServices] ZaloDbContext dbContext)
        {
            var messages = await dbContext.ZaloMessages
                .OrderByDescending(m => m.Time)
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .Select(m => new
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    RecipientId = m.RecipientId,
                    Content = m.Content,
                    Time = m.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = m.Direction,
                    SenderName = m.Sender != null ? m.Sender.Name : null,
                    SenderAvatar = m.Sender != null ? m.Sender.AvatarUrl : null,
                    RecipientName = m.Recipient != null ? m.Recipient.Name : null,
                    RecipientAvatar = m.Recipient != null ? m.Recipient.AvatarUrl : null
                })
                .ToListAsync();
            return Ok(messages);
        }

        [HttpGet("messages/customer/{customerId}")]
        public async Task<IActionResult> GetMessagesByCustomer([FromServices] ZaloDbContext dbContext, string customerId)
        {
            var messages = await dbContext.ZaloMessages
                .Where(m => m.SenderId == customerId || m.RecipientId == customerId)
                .OrderByDescending(m => m.Time)
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .Select(m => new
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    RecipientId = m.RecipientId,
                    Content = m.Content,
                    Time = m.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = m.Direction,
                    SenderName = m.Sender != null ? m.Sender.Name : null,
                    SenderAvatar = m.Sender != null ? m.Sender.AvatarUrl : null,
                    RecipientName = m.Recipient != null ? m.Recipient.Name : null,
                    RecipientAvatar = m.Recipient != null ? m.Recipient.AvatarUrl : null
                })
                .ToListAsync();
            return Ok(messages);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromServices] ZaloDbContext dbContext)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(Request.Body))
                {
                    body = await reader.ReadToEndAsync();
                }

                _logger.LogInformation("Received Zalo webhook: {Body}", body);

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("event_name", out var eventNameElement))
                {
                    var eventName = eventNameElement.GetString();
                    switch (eventName)
                    {
                        case "user_send_text":
                            await ProcessTextMessage(dbContext, root);
                            break;
                        case "oa_send_message":
                            await ProcessSendMessageConfirmation(dbContext, root);
                            break;
                        case "user_send_file":
                            await ProcessFileMessage(dbContext, root);
                            break;
                        case "user_send_image":
                            await ProcessImageMessage(dbContext, root);
                            break;
                        case "oa_send_image":
                            await ProcessSendMessageConfirmation(dbContext, root);
                            break;
                        default:
                            _logger.LogWarning("Unknown event: {EventName}", eventName);
                            break;
                    }
                }

                return Ok(new { status = "success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Zalo webhook");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Gửi tin nhắn văn bản đến người dùng ẩn danh
        [HttpPost("send-anonymous-text")]
        public async Task<IActionResult> SendAnonymousText([FromServices] ZaloDbContext dbContext, [FromBody] ZaloMessageRequest request)

        {
            if (string.IsNullOrEmpty(request.AnonymousId) || string.IsNullOrEmpty(request.ConversationId) || string.IsNullOrEmpty(request.Message))
                return BadRequest(new { status = "error", details = "AnonymousId, ConversationId và Message là bắt buộc." });

            var url = "https://openapi.zalo.me/v2.0/oa/message";
            var payload = new
            {
                recipient = new
                {
                    anonymous_id = request.AnonymousId,
                    conversation_id = request.ConversationId
                },
                message = new { text = request.Message }
            };

            var client = _httpClientFactory.CreateClient();
            var accessToken = await _zaloAuthService.GetAccessTokenAsync();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("access_token", accessToken);

            var payloadJson = JsonSerializer.Serialize(payload);
            var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Lưu lịch sử gửi tin nhắn nếu cần
            if (response.IsSuccessStatusCode)
            {
                var oaAsCustomer = await EnsureOACustomerExistsAsync(dbContext);
                var zaloMessage = new ZaloMessage
                {
                    SenderId = _oaId,
                    RecipientId = request.AnonymousId,
                    Content = request.Message,
                    Time = DateTime.UtcNow,
                    Direction = "outbound"
                };
                dbContext.ZaloMessages.Add(zaloMessage);
                await dbContext.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveZaloMessage", new
                {
                    Id = zaloMessage.Id,
                    SenderId = zaloMessage.SenderId,
                    RecipientId = zaloMessage.RecipientId,
                    Content = zaloMessage.Content,
                    Time = zaloMessage.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = zaloMessage.Direction,
                    SenderName = oaAsCustomer.Name,
                    SenderAvatar = oaAsCustomer.AvatarUrl
                });

                return Ok(new { status = "success", details = responseContent });
            }

            return StatusCode((int)response.StatusCode, new { status = "error", details = responseContent });
        }

        // Gửi tin nhắn dạng ảnh đến người dùng ẩn danh
        [HttpPost("send-anonymous-image")]
        public async Task<IActionResult> SendAnonymousImage([FromServices] ZaloDbContext dbContext, [FromForm] string anonymousId, [FromForm] string conversationId, [FromForm] IFormFile image)
        {
            if (image == null || image.Length == 0) return BadRequest("File ảnh trống.");
            if (string.IsNullOrEmpty(anonymousId) || string.IsNullOrEmpty(conversationId)) return BadRequest("anonymousId và conversationId là bắt buộc.");

            var client = _httpClientFactory.CreateClient();
            var accessToken = await _zaloAuthService.GetAccessTokenAsync();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("access_token", accessToken);

            // Upload ảnh lên Zalo
            var uploadEndpoint = "https://openapi.zalo.me/v2.0/oa/upload/image";
            using var form = new MultipartFormDataContent();
            using var fs = image.OpenReadStream();
            form.Add(new StreamContent(fs), "file", image.FileName);

            var uploadResponse = await client.PostAsync(uploadEndpoint, form);
            var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
            if (!uploadResponse.IsSuccessStatusCode)
                return StatusCode((int)uploadResponse.StatusCode, new { status = "error", details = uploadJson });

            var doc = JsonDocument.Parse(uploadJson);
            var attachmentId = doc.RootElement.GetProperty("data").GetProperty("attachment_id").GetString();

            // Gửi tin nhắn ảnh
            var url = "https://openapi.zalo.me/v2.0/oa/message";
            var payload = new
            {
                recipient = new
                {
                    anonymous_id = anonymousId,
                    conversation_id = conversationId
                },
                message = new
                {
                    attachments = new[]
                    {
                        new
                        {
                            type = "image",
                            payload = new { attachment_id = attachmentId }
                        }
                    }
                }
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Lưu lịch sử gửi tin nhắn
            if (response.IsSuccessStatusCode)
            {
                var oaAsCustomer = await EnsureOACustomerExistsAsync(dbContext);
                var zaloMessage = new ZaloMessage
                {
                    SenderId = _oaId,
                    RecipientId = anonymousId,
                    Content = attachmentId,
                    Time = DateTime.UtcNow,
                    Direction = "outbound"
                };
                dbContext.ZaloMessages.Add(zaloMessage);
                await dbContext.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveZaloMessage", new
                {
                    Id = zaloMessage.Id,
                    SenderId = zaloMessage.SenderId,
                    RecipientId = zaloMessage.RecipientId,
                    Content = zaloMessage.Content,
                    Time = zaloMessage.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = zaloMessage.Direction,
                    SenderName = oaAsCustomer.Name,
                    SenderAvatar = oaAsCustomer.AvatarUrl,
                    FileName = image.FileName,
                    FileType = image.ContentType,
                    IsImage = true
                });

                return Ok(new { status = "success", details = responseContent });
            }

            return StatusCode((int)response.StatusCode, new { status = "error", details = responseContent });
        }

        // Gửi tin nhắn dạng file đến người dùng ẩn danh
        [HttpPost("send-anonymous-file")]
        public async Task<IActionResult> SendAnonymousFile([FromServices] ZaloDbContext dbContext, [FromForm] string anonymousId, [FromForm] string conversationId, [FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("File trống.");
            if (string.IsNullOrEmpty(anonymousId) || string.IsNullOrEmpty(conversationId)) return BadRequest("anonymousId và conversationId là bắt buộc.");

            var client = _httpClientFactory.CreateClient();
            var accessToken = await _zaloAuthService.GetAccessTokenAsync();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("access_token", accessToken);

            // Upload file lên Zalo
            var uploadEndpoint = "https://openapi.zalo.me/v2.0/oa/upload/file";
            using var form = new MultipartFormDataContent();
            using var fs = file.OpenReadStream();
            form.Add(new StreamContent(fs), "file", file.FileName);

            var uploadResponse = await client.PostAsync(uploadEndpoint, form);
            var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
            if (!uploadResponse.IsSuccessStatusCode)
                return StatusCode((int)uploadResponse.StatusCode, new { status = "error", details = uploadJson });

            var doc = JsonDocument.Parse(uploadJson);
            var attachmentId = doc.RootElement.GetProperty("data").GetProperty("attachment_id").GetString();

            // Gửi tin nhắn file
            var url = "https://openapi.zalo.me/v2.0/oa/message";
            var payload = new
            {
                recipient = new
                {
                    anonymous_id = anonymousId,
                    conversation_id = conversationId
                },
                message = new
                {
                    attachments = new[]
                    {
                        new
                        {
                            type = "file",
                            payload = new { attachment_id = attachmentId }
                        }
                    }
                }
            };

            var payloadJson = JsonSerializer.Serialize(payload);
            var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Lưu lịch sử gửi tin nhắn
            if (response.IsSuccessStatusCode)
            {
                var oaAsCustomer = await EnsureOACustomerExistsAsync(dbContext);
                var zaloMessage = new ZaloMessage
                {
                    SenderId = _oaId,
                    RecipientId = anonymousId,
                    Content = attachmentId,
                    Time = DateTime.UtcNow,
                    Direction = "outbound"
                };
                dbContext.ZaloMessages.Add(zaloMessage);
                await dbContext.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveZaloMessage", new
                {
                    Id = zaloMessage.Id,
                    SenderId = zaloMessage.SenderId,
                    RecipientId = zaloMessage.RecipientId,
                    Content = zaloMessage.Content,
                    Time = zaloMessage.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = zaloMessage.Direction,
                    SenderName = oaAsCustomer.Name,
                    SenderAvatar = oaAsCustomer.AvatarUrl,
                    FileName = file.FileName,
                    FileType = file.ContentType,
                    IsImage = false
                });

                return Ok(new { status = "success", details = responseContent });
            }

            return StatusCode((int)response.StatusCode, new { status = "error", details = responseContent });
        }

        // Helper method (nếu cần)
        private async Task<ZaloCustomer> EnsureOACustomerExistsAsync(ZaloDbContext dbContext)
        {
            var oaId = _oaId;
            var oaAsCustomer = await dbContext.ZaloCustomers.FindAsync(oaId);
            if (oaAsCustomer == null)
            {
                oaAsCustomer = new ZaloCustomer
                {
                    ZaloId = oaId,
                    Name = "My OA",
                    AvatarUrl = "",
                    LastUpdated = DateTime.UtcNow
                };
                dbContext.ZaloCustomers.Add(oaAsCustomer);
                await dbContext.SaveChangesAsync();
            }
            return oaAsCustomer;
        }

        private string GetAttachmentType(string contentType)
        {
            return contentType.StartsWith("image") ? "image" :
                   contentType.StartsWith("video") ? "video" :
                   contentType.StartsWith("audio") ? "audio" : "file";
        }

        // Các hàm xử lý webhook inbound giữ nguyên như cũ...

        private async Task<ZaloCustomer> GetOrCreateZaloCustomerAsync(ZaloDbContext dbContext, string userId)
        {
            var customer = await dbContext.ZaloCustomers.FindAsync(userId);

            if (customer == null || customer.LastUpdated < DateTime.UtcNow.AddDays(-1))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var accessToken = await _zaloAuthService.GetAccessTokenAsync();
                    var url = $"https://openapi.zalo.me/v2.0/oa/getprofile?user_id={userId}";
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("access_token", accessToken);

                    var response = await client.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(content);
                        var data = doc.RootElement;

                        if (data.TryGetProperty("error", out var error) && error.GetInt32() == 0)
                        {
                            var name = data.GetProperty("data").GetProperty("display_name").GetString();
                            var avatarUrl = data.GetProperty("data").TryGetProperty("avatar", out var avatar)
                                ? avatar.GetString()
                                : "";

                            if (customer == null)
                            {
                                customer = new ZaloCustomer { ZaloId = userId };
                                dbContext.ZaloCustomers.Add(customer);
                            }

                            customer.Name = name ?? $"User {userId}";
                            customer.AvatarUrl = avatarUrl ?? "";
                            customer.LastUpdated = DateTime.UtcNow;

                            await dbContext.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception khi lấy profile Zalo cho userId: {UserId}", userId);
                }

                if (customer == null)
                {
                    customer = new ZaloCustomer
                    {
                        ZaloId = userId,
                        Name = $"User {userId}",
                        AvatarUrl = "",
                        LastUpdated = DateTime.UtcNow
                    };
                    dbContext.ZaloCustomers.Add(customer);
                    await dbContext.SaveChangesAsync();
                }
            }
            return customer;
        }

        // Các hàm xử lý webhook inbound giữ nguyên như cũ...
        private async Task ProcessTextMessage(ZaloDbContext dbContext, JsonElement data)
        {
            var senderId = data.GetProperty("sender").GetProperty("id").GetString();
            var message = data.GetProperty("message").GetProperty("text").GetString();
            var timestampStr = data.GetProperty("timestamp").GetString();
            var timestampLong = long.Parse(timestampStr);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).UtcDateTime;

            var customer = await GetOrCreateZaloCustomerAsync(dbContext, senderId);
            var oaCustomer = await EnsureOACustomerExistsAsync(dbContext);

            var zaloMessage = new ZaloMessage
            {
                SenderId = senderId,
                RecipientId = _oaId,
                Content = message,
                Time = timestamp,
                Direction = "inbound"
            };
            dbContext.ZaloMessages.Add(zaloMessage);
            await dbContext.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("ReceiveZaloMessage", new
            {
                Id = zaloMessage.Id,
                SenderId = zaloMessage.SenderId,
                RecipientId = zaloMessage.RecipientId,
                Content = zaloMessage.Content,
                Time = zaloMessage.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                Direction = zaloMessage.Direction,
                SenderName = customer.Name,
                SenderAvatar = customer.AvatarUrl
            });
        }

        private async Task ProcessFileMessage(ZaloDbContext dbContext, JsonElement data)
        {
            var senderId = data.GetProperty("sender").GetProperty("id").GetString();
            var attachments = data.GetProperty("message").GetProperty("attachments");
            var firstAttachment = attachments[0];
            var payload = firstAttachment.GetProperty("payload");
            var fileUrl = payload.GetProperty("url").GetString();
            var fileName = payload.GetProperty("name").GetString();
            var fileType = payload.GetProperty("type").GetString();
            var timestampStr = data.GetProperty("timestamp").GetString();
            var timestampLong = long.Parse(timestampStr);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).UtcDateTime;

            var customer = await GetOrCreateZaloCustomerAsync(dbContext, senderId);

            var isImage = fileType.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                || fileType.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                || fileType.Equals("png", StringComparison.OrdinalIgnoreCase)
                || fileType.Equals("gif", StringComparison.OrdinalIgnoreCase)
                || fileType.Equals("bmp", StringComparison.OrdinalIgnoreCase)
                || fileType.Equals("webp", StringComparison.OrdinalIgnoreCase);

            var zaloMessage = new ZaloMessage
            {
                SenderId = senderId,
                RecipientId = _oaId,
                Content = fileUrl,
                Time = timestamp,
                Direction = "inbound"
            };
            dbContext.ZaloMessages.Add(zaloMessage);
            await dbContext.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("ReceiveZaloMessage", new
            {
                Id = zaloMessage.Id,
                SenderId = zaloMessage.SenderId,
                RecipientId = zaloMessage.RecipientId,
                Content = zaloMessage.Content,
                Time = zaloMessage.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                Direction = zaloMessage.Direction,
                SenderName = customer.Name,
                SenderAvatar = customer.AvatarUrl,
                FileName = fileName,
                FileType = fileType,
                IsImage = isImage
            });
        }

        private async Task ProcessImageMessage(ZaloDbContext dbContext, JsonElement data)
        {
            var senderId = data.GetProperty("sender").GetProperty("id").GetString();
            var timestampStr = data.GetProperty("timestamp").GetString();
            var timestampLong = long.Parse(timestampStr);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).UtcDateTime;

            var attachments = data.GetProperty("message").GetProperty("attachments");
            foreach (var attachment in attachments.EnumerateArray())
            {
                var payload = attachment.GetProperty("payload");
                var imageUrl = payload.GetProperty("url").GetString();

                var customer = await GetOrCreateZaloCustomerAsync(dbContext, senderId);

                var zaloMessage = new ZaloMessage
                {
                    SenderId = senderId,
                    RecipientId = _oaId,
                    Content = imageUrl,
                    Time = timestamp,
                    Direction = "inbound"
                };
                dbContext.ZaloMessages.Add(zaloMessage);
                await dbContext.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveZaloMessage", new
                {
                    Id = zaloMessage.Id,
                    SenderId = zaloMessage.SenderId,
                    RecipientId = zaloMessage.RecipientId,
                    Content = zaloMessage.Content,
                    Time = zaloMessage.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = zaloMessage.Direction,
                    SenderName = customer.Name,
                    SenderAvatar = customer.AvatarUrl
                });
            }
        }

        private async Task ProcessSendMessageConfirmation(ZaloDbContext dbContext, JsonElement data)
        {
            // Implement logic if needed for delivery confirmation
        }
    }
}