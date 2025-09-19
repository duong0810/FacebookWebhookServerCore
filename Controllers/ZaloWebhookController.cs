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
                        case "oa_send_file":
                            await ProcessOASendFile(dbContext, root);
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

        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromServices] ZaloDbContext dbContext, [FromBody] ZaloMessageRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.RecipientId) || string.IsNullOrEmpty(request.Message))
                    return BadRequest(new { status = "error", details = "RecipientId and Message are required." });

                var url = "https://openapi.zalo.me/v3.0/oa/message/cs";
                var payload = new
                {
                    recipient = new { user_id = request.RecipientId },
                    message = new { text = request.Message }
                };

                var client = _httpClientFactory.CreateClient();
                var accessToken = await _zaloAuthService.GetAccessTokenAsync();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("access_token", accessToken);

                var payloadJson = JsonSerializer.Serialize(payload);
                _logger.LogInformation("Payload gửi lên Zalo: {Payload}", payloadJson);

                var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Response từ Zalo: {Response}", responseContent);

                if (response.IsSuccessStatusCode)
                {
                    var oaAsCustomer = await EnsureOACustomerExistsAsync(dbContext);

                    var zaloMessage = new ZaloMessage
                    {
                        SenderId = _oaId,
                        RecipientId = request.RecipientId,
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to Zalo");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("send-attachment")]
        public async Task<IActionResult> SendAttachment(
        [FromServices] ZaloDbContext dbContext,
        [FromForm] string recipientId,
        [FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("File is empty.");

            try
            {
                // Log giá trị ContentType để debug
                _logger.LogInformation($"[DEBUG] file.ContentType: {file.ContentType}, file.Name: {file.Name}, file.FileName: {file.FileName}");

                var client = _httpClientFactory.CreateClient();
                var accessToken = await _zaloAuthService.GetAccessTokenAsync();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("access_token", accessToken);

                object payload;
                string contentForDb = "";
                string fileType = file.ContentType.ToLower();


                if (fileType.StartsWith("image/"))
                {
                    // Upload ảnh lên Cloudinary để lấy link public
                    var uploadParams = new ImageUploadParams
                    {
                        File = new FileDescription(file.FileName, file.OpenReadStream())
                    };
                    var uploadResult = _cloudinary.Upload(uploadParams);
                    if (uploadResult.StatusCode != System.Net.HttpStatusCode.OK)
                        return StatusCode(500, new { status = "error", details = "Upload image failed" });

                    var imageUrl = uploadResult.SecureUrl?.ToString();
                    if (string.IsNullOrEmpty(imageUrl))
                        return StatusCode(500, new { status = "error", details = "Cannot get image url" });

                    // Tạo payload gửi ảnh (template/media)
                    payload = new
                    {
                        recipient = new { user_id = recipientId },
                        message = new
                        {
                            attachment = new
                            {
                                type = "template",
                                payload = new
                                {
                                    template_type = "media",
                                    elements = new[]
                                    {
                        new
                        {
                            media_type = "image",
                            url = imageUrl
                        }
                    }
                                }
                            }
                        }
                    };
                    contentForDb = imageUrl;
                }
                else
                {
                    // Upload file lên Cloudinary để lấy link public
                    var uploadParams = new RawUploadParams
                    {
                        File = new FileDescription(file.FileName, file.OpenReadStream())
                    };
                    var uploadResult = _cloudinary.Upload(uploadParams);
                    if (uploadResult.StatusCode != System.Net.HttpStatusCode.OK)
                        return StatusCode(500, new { status = "error", details = "Upload file failed" });

                    var fileUrl = uploadResult.SecureUrl?.ToString();
                    if (string.IsNullOrEmpty(fileUrl))
                        return StatusCode(500, new { status = "error", details = "Cannot get file url" });

                    // Upload file lên Zalo để lấy token (nếu vẫn cần gửi file qua Zalo)
                    var uploadEndpoint = "https://openapi.zalo.me/v2.0/oa/upload/file";
                    using var form = new MultipartFormDataContent();
                    using var fs = file.OpenReadStream();
                    form.Add(new StreamContent(fs), "file", file.FileName);

                    var uploadResponse = await client.PostAsync(uploadEndpoint, form);
                    var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
                    _logger.LogInformation("Upload file response: {UploadJson}", uploadJson);

                    if (!uploadResponse.IsSuccessStatusCode)
                        return StatusCode((int)uploadResponse.StatusCode, new { status = "error", details = uploadJson });

                    var doc = JsonDocument.Parse(uploadJson);
                    if (!doc.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("token", out var tokenElement))
                        return BadRequest("Invalid upload response: missing token");

                    var fileToken = tokenElement.GetString();

                    // Tạo payload gửi file cho Zalo (nếu cần gửi file qua Zalo)
                    payload = new
                    {
                        recipient = new { user_id = recipientId },
                        message = new
                        {
                            attachment = new
                            {
                                type = "file",
                                payload = new
                                {
                                    token = fileToken
                                }
                            }
                        }
                    };
                    contentForDb = fileUrl; // Lưu url tải file thay vì token
                }

                var payloadJson = JsonSerializer.Serialize(payload);
                _logger.LogInformation("Payload gửi lên Zalo: {Payload}", payloadJson);

                var url = "https://openapi.zalo.me/v3.0/oa/message/cs";
                var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Response từ Zalo: {Response}", responseContent);

                if (response.IsSuccessStatusCode)
                {
                    var oaAsCustomer = await EnsureOACustomerExistsAsync(dbContext);

                    var zaloMessage = new ZaloMessage
                    {
                        SenderId = _oaId,
                        RecipientId = recipientId,
                        Content = contentForDb,
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
                        FileType = file.ContentType,
                        IsImage = fileType.StartsWith("image/"),
                        IsFile = !fileType.StartsWith("image/")
                    });

                    return Ok(new { status = "success", url = contentForDb });
                }

                return StatusCode((int)response.StatusCode, new { status = "error", details = responseContent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending attachment to Zalo");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Helper method (nếu cần)
        private async Task<ZaloCustomer> EnsureOACustomerExistsAsync(ZaloDbContext dbContext)
        {
            var oaId = _oaId; // Thay bằng OA ID thực tế
            var oaAsCustomer = await dbContext.ZaloCustomers.FindAsync(oaId);
            if (oaAsCustomer == null)
            {
                oaAsCustomer = new ZaloCustomer
                {
                    ZaloId = oaId,
                    Name = "My OA", // Tùy chỉnh tên OA
                    AvatarUrl = "", // Thêm URL avatar nếu có
                    LastUpdated = DateTime.UtcNow
                };
                dbContext.ZaloCustomers.Add(oaAsCustomer);
                await dbContext.SaveChangesAsync();
            }
            return oaAsCustomer;
        }

        // Helper method (tái sử dụng từ code cũ nếu có)
        private string GetAttachmentType(string contentType)
        {
            return contentType.StartsWith("image") ? "image" :
                   contentType.StartsWith("video") ? "video" :
                   contentType.StartsWith("audio") ? "audio" : "file";
        }

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




        private async Task ProcessTextMessage(ZaloDbContext dbContext, JsonElement data)
        {
            var senderId = data.GetProperty("sender").GetProperty("id").GetString();
            var message = data.GetProperty("message").GetProperty("text").GetString();
            var timestampStr = data.GetProperty("timestamp").GetString();
            var timestampLong = long.Parse(timestampStr);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).UtcDateTime;

            // Đảm bảo cả OA và người dùng đều tồn tại trong ZaloCustomers
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
        private async Task ProcessOASendImage(ZaloDbContext dbContext, JsonElement data)
        {
            var recipientId = data.GetProperty("recipient").GetProperty("id").GetString();
            var timestampStr = data.GetProperty("timestamp").GetString();
            var timestampLong = long.Parse(timestampStr);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).UtcDateTime;

            var attachments = data.GetProperty("message").GetProperty("attachments");
            var oaCustomer = await EnsureOACustomerExistsAsync(dbContext);
            var recipientCustomer = await GetOrCreateZaloCustomerAsync(dbContext, recipientId);

            foreach (var attachment in attachments.EnumerateArray())
            {
                var payload = attachment.GetProperty("payload");
                // Zalo không trả url khi OA gửi, thay vào đó dùng attachment_id hoặc để trống nếu chỉ xác nhận
                var attachmentId = payload.TryGetProperty("attachment_id", out var attachmentIdElement)
                    ? attachmentIdElement.GetString()
                    : "";

                var zaloMessage = new ZaloMessage
                {
                    SenderId = _oaId,
                    RecipientId = recipientId,
                    Content = attachmentId, // Lưu attachment_id thay vì url
                    Time = timestamp,
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
                    SenderName = oaCustomer.Name,
                    SenderAvatar = oaCustomer.AvatarUrl,
                    IsImage = true // Giả định là image từ event oa_send_image
                });
            }
        }
        private async Task ProcessOASendFile(ZaloDbContext dbContext, JsonElement data)
        {
            var recipientId = data.GetProperty("recipient").GetProperty("id").GetString();
            var timestampStr = data.GetProperty("timestamp").GetString();
            var timestampLong = long.Parse(timestampStr);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).UtcDateTime;

            var attachments = data.GetProperty("message").GetProperty("attachments");
            var oaCustomer = await EnsureOACustomerExistsAsync(dbContext);
            var recipientCustomer = await GetOrCreateZaloCustomerAsync(dbContext, recipientId);

            foreach (var attachment in attachments.EnumerateArray())
            {
                var payload = attachment.GetProperty("payload");
                var fileUrl = payload.GetProperty("url").GetString();
                var fileName = payload.GetProperty("name").GetString();
                var fileType = payload.GetProperty("type").GetString();

                var zaloMessage = new ZaloMessage
                {
                    SenderId = _oaId,
                    RecipientId = recipientId,
                    Content = fileUrl,
                    Time = timestamp,
                    Direction = "outbound"
                };
                dbContext.ZaloMessages.Add(zaloMessage);
                await dbContext.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveZaloMessage", new
                {
                    Id = zaloMessage.Id,
                    SenderId = zaloMessage.SenderId,
                    RecipientId = zaloMessage.RecipientId,
                    Content = zaloMessage.Content, // chính là url tải file
                    Time = zaloMessage.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = zaloMessage.Direction,
                    SenderName = oaCustomer.Name,
                    SenderAvatar = oaCustomer.AvatarUrl,
                    FileType = fileType,
                    IsImage = fileType.StartsWith("image/"),
                    IsFile = !fileType.StartsWith("image/")
                });
            }
        }
        private async Task ProcessSendMessageConfirmation(ZaloDbContext dbContext, JsonElement data)
        {
            // Implement logic if needed for delivery confirmation
        }


    }
}