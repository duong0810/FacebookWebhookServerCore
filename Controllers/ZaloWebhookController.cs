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
using System.Security.Cryptography;
using System.Text;
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
            try
            {
                _logger.LogInformation("Received Zalo domain verification request");
                foreach (var param in Request.Query)
                {
                    _logger.LogInformation("Query parameter: {Key} = {Value}", param.Key, param.Value);
                }
                return Ok("Zalo webhook verification successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Zalo domain verification");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("check-token")]
        public async Task<IActionResult> CheckToken([FromServices] ZaloDbContext dbContext)
        {
            var token = await dbContext.ZaloTokens.FirstOrDefaultAsync();
            if (token == null)
                return Ok("Chưa có token trong DB");
            return Ok(new
            {
                AccessToken = string.IsNullOrEmpty(token.AccessToken) ? "Không có" : "Đã có",
                RefreshToken = string.IsNullOrEmpty(token.RefreshToken) ? "Không có" : "Đã có",
                ExpireAt = token.ExpireAt
            });
        }

        [HttpGet("messages")]
        public async Task<IActionResult> GetAllMessages([FromServices] ZaloDbContext dbContext)
        {
            var messages = await dbContext.ZaloMessages
                .OrderByDescending(m => m.Time)
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .ToListAsync();

            var result = messages.Select(m => new
            {
                m.Id,
                m.SenderId,
                m.RecipientId,
                m.Content,
                Time = m.TimeVietnam.ToString("dd/MM/yyyy HH:mm:ss"),
                m.Direction,
                SenderName = m.Sender?.Name,
                SenderAvatar = m.Sender?.AvatarUrl,
                RecipientName = m.Recipient?.Name,
                RecipientAvatar = m.Recipient?.AvatarUrl
            });
            return Ok(result);
        }

        [HttpGet("messages/customer/{customerId}")]
        public async Task<IActionResult> GetMessagesByCustomerId([FromServices] ZaloDbContext dbContext, string customerId)
        {
            var messages = await dbContext.ZaloMessages
                .Where(m => m.SenderId == customerId || m.RecipientId == customerId)
                .OrderByDescending(m => m.Time)
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .ToListAsync();

            var result = messages.Select(m => new
            {
                m.Id,
                m.SenderId,
                m.RecipientId,
                m.Content,
                Time = m.TimeVietnam.ToString("dd/MM/yyyy HH:mm:ss"),
                m.Direction,
                SenderName = m.Sender?.Name,
                SenderAvatar = m.Sender?.AvatarUrl,
                RecipientName = m.Recipient?.Name,
                RecipientAvatar = m.Recipient?.AvatarUrl
            });

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromServices] ZaloDbContext dbContext)
        {
            try
            {
                string requestBody;
                using (var reader = new StreamReader(Request.Body))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                _logger.LogInformation("Received Zalo webhook: {Body}", requestBody);

                if (Request.Headers.TryGetValue("X-ZaloOA-Signature", out var signature))
                {
                    if (!VerifySignature(requestBody, signature))
                    {
                        _logger.LogWarning("Invalid Zalo webhook signature");
                        return Unauthorized("Invalid signature");
                    }
                }

                using var jsonDoc = JsonDocument.Parse(requestBody);
                var root = jsonDoc.RootElement;

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
                        case "user_send_image": // THÊM CASE NÀY
                            await ProcessImageMessage(dbContext, root);
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

        [HttpPost("init-token")]
        public async Task<IActionResult> InitToken([FromServices] ZaloDbContext dbContext, [FromBody] ZaloTokenInfo token)
        {
            dbContext.ZaloTokens.RemoveRange(dbContext.ZaloTokens);
            await dbContext.SaveChangesAsync();
            dbContext.ZaloTokens.Add(token);
            await dbContext.SaveChangesAsync();
            return Ok("Đã lưu access_token và refresh_token vào DB.");
        }

        private async Task ProcessTextMessage(ZaloDbContext dbContext, JsonElement data)
        {
            try
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
                    Time = zaloMessage.TimeVietnam.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = zaloMessage.Direction,
                    SenderName = customer.Name,
                    SenderAvatar = customer.AvatarUrl
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing text message");
                throw;
            }
        }

        private async Task ProcessSendMessageConfirmation(ZaloDbContext dbContext, JsonElement data)
        {
            try
            {
                var userId = data.GetProperty("user_id").GetString();
                var messageObj = data.GetProperty("message");
                var msgId = messageObj.GetProperty("msg_id").GetString();
                var text = messageObj.GetProperty("text").GetString();
                var msgTime = messageObj.GetProperty("time").GetInt64();

                var outboundMsg = await dbContext.ZaloMessages
                    .FirstOrDefaultAsync(m => m.SenderId == _oaId && m.RecipientId == userId && m.Content == text && m.Direction == "outbound");
                if (outboundMsg != null)
                {
                    // Đã xoá outboundMsg.Status và outboundMsg.DeliveredTime
                    await dbContext.SaveChangesAsync();

                    await _hubContext.Clients.All.SendAsync("MessageDelivered", new
                    {
                        MsgId = msgId,
                        UserId = userId,
                        Timestamp = TimeZoneInfo.ConvertTimeFromUtc(
                            DateTimeOffset.FromUnixTimeMilliseconds(msgTime).UtcDateTime,
                            TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")
                        ).ToString("dd/MM/yyyy HH:mm:ss")
                    });
                }
                _logger.LogInformation("Processed oa_send_message for {UserId}: {Text}", userId, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing send message confirmation");
            }
        }

        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromServices] ZaloDbContext dbContext, [FromBody] ZaloMessageRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.RecipientId) || string.IsNullOrEmpty(request.Message))
                {
                    _logger.LogWarning("Invalid request: RecipientId or Message is empty.");
                    return BadRequest(new { status = "error", details = "RecipientId and Message are required." });
                }

                var client = _httpClientFactory.CreateClient();
                var url = "https://openapi.zalo.me/v3.0/oa/message/cs";
                var payload = new
                {
                    recipient = new { user_id = request.RecipientId },
                    message = new { text = request.Message }
                };

                var payloadJson = JsonSerializer.Serialize(payload);
                _logger.LogInformation("Zalo payload gửi đi: {Payload}", payloadJson);

                var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                // Lấy access token và log chi tiết
                var accessToken = await _zaloAuthService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogError("Failed to get access token.");
                    return StatusCode(500, new { status = "error", details = "Failed to retrieve access token." });
                }
                _logger.LogInformation("Zalo access_token sử dụng: {AccessToken}", accessToken.Substring(0, 10) + "...");

                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("access_token", accessToken);

                var response = await client.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Zalo API response: {ResponseContent}", responseContent);

                // Xử lý response từ Zalo
                try
                {
                    using var doc = JsonDocument.Parse(responseContent);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        var errorValue = errorElement.ValueKind == JsonValueKind.Number
                            ? errorElement.GetInt32().ToString()
                            : errorElement.GetString();
                        if (errorValue != "0")
                        {
                            var errorMsg = root.TryGetProperty("message", out var msgElement)
                                ? msgElement.GetString()
                                : "Unknown error";
                            _logger.LogWarning("Zalo API error (send-attachment): {Error} - {Message}", errorValue, errorMsg);
                        }
                        else
                        {
                            _logger.LogInformation("Zalo API gửi file/hình ảnh thành công.");
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Lỗi parse responseContent từ Zalo API (send-attachment)");
                }

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
                        Time = zaloMessage.TimeVietnam.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
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

        private async Task<ZaloCustomer> GetOrCreateZaloCustomerAsync(ZaloDbContext dbContext, string userId)
        {
            var customer = await dbContext.ZaloCustomers.FindAsync(userId);

            if (customer == null || customer.LastUpdated < DateTime.UtcNow.AddDays(-1))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var accessToken = await _zaloAuthService.GetAccessTokenAsync();

                    // Sử dụng API v2.0 và truyền user_id qua query string
                    var url = $"https://openapi.zalo.me/v2.0/oa/getprofile?user_id={userId}";
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("access_token", accessToken);

                    var response = await client.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation("Zalo profile response: {Content}", content);

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
                            _logger.LogInformation("Đã lưu avatar vào DB cho userId: {UserId}, AvatarUrl: {AvatarUrl}", userId, customer.AvatarUrl);
                        }
                        else
                        {
                            _logger.LogWarning("Zalo API trả về lỗi cho userId: {UserId}, error: {Error}", userId, error.GetInt32());
                        }
                    }
                    else
                    {
                        _logger.LogError("Không gọi được Zalo API getprofile cho userId: {UserId}, StatusCode: {StatusCode}", userId, response.StatusCode);
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
                    _logger.LogWarning("Tạo mới customer nhưng không có avatar cho userId: {UserId}", userId);
                }
            }
            else
            {
                _logger.LogInformation("Customer đã có trong DB, AvatarUrl: {AvatarUrl}", customer.AvatarUrl);
            }

            return customer;
        }

        private async Task<ZaloCustomer> EnsureOACustomerExistsAsync(ZaloDbContext dbContext)
        {
            var oaCustomer = await dbContext.ZaloCustomers.FindAsync(_oaId);
            if (oaCustomer == null)
            {
                oaCustomer = new ZaloCustomer
                {
                    ZaloId = _oaId,
                    Name = "KOSMOSOS software", // Tên OA của bạn
                    AvatarUrl = "",
                    LastUpdated = DateTime.UtcNow
                };
                dbContext.ZaloCustomers.Add(oaCustomer);
                await dbContext.SaveChangesAsync();
            }
            return oaCustomer;
        }

        private bool VerifySignature(string payload, string signature)
        {
            try
            {
                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_oaSecret)))
                {
                    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                    var computedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();
                    return computedSignature == signature.ToLower();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying Zalo signature");
                return false;
            }
        }
        [HttpPost("send-attachment")]
        public async Task<IActionResult> SendAttachment(
    [FromServices] ZaloDbContext dbContext,
    [FromForm] string recipientId,
    [FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File rỗng.");

            try
            {
                // Upload file lên Cloudinary
                var fileUrl = await UploadFileToCloudinaryAsync(file);
                var attachmentType = GetAttachmentType(file.ContentType);

                // Đảm bảo OA và người nhận tồn tại trong bảng ZaloCustomer
                var oaAsCustomer = await EnsureOACustomerExistsAsync(dbContext);
                var recipientCustomer = await GetOrCreateZaloCustomerAsync(dbContext, recipientId);

                // Tạo payload gửi lên Zalo OA API
                var url = "https://openapi.zalo.me/v3.0/oa/message/cs";
                var payload = new
                {
                    recipient = new { user_id = recipientId },
                    message = new
                    {
                        text = file.FileName, // hoặc mô tả ngắn
                        attachment = new
                        {
                            type = attachmentType, // "image", "file", "video", ...
                            payload = new { url = fileUrl }
                        }
                    }
                };

                var client = _httpClientFactory.CreateClient();
                var accessToken = await _zaloAuthService.GetAccessTokenAsync();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("access_token", accessToken);

                var payloadJson = JsonSerializer.Serialize(payload);
                var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                // Lưu vào DB
                var zaloMessage = new ZaloMessage
                {
                    SenderId = _oaId,
                    RecipientId = recipientId,
                    Content = fileUrl,
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
                    Time = zaloMessage.TimeVietnam.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = zaloMessage.Direction,
                    SenderName = oaAsCustomer.Name,
                    SenderAvatar = oaAsCustomer.AvatarUrl,
                    FileName = file.FileName,
                    FileType = file.ContentType,
                    IsImage = attachmentType == "image"
                });

                if (response.IsSuccessStatusCode)
                    return Ok(new { status = "success", details = responseContent });

                return StatusCode((int)response.StatusCode, new { status = "error", details = responseContent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi file/hình ảnh tới Zalo");
                return StatusCode(500, new { error = ex.Message, inner = ex.InnerException?.Message });
            }
        }
        private async Task<string> UploadFileToCloudinaryAsync(IFormFile file)
        {
            if (file.Length > 0)
            {
                using (var stream = file.OpenReadStream())
                {
                    if (file.ContentType.StartsWith("image/"))
                    {
                        var uploadParams = new ImageUploadParams()
                        {
                            File = new FileDescription(file.FileName, stream)
                        };
                        var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                        if (uploadResult.Error != null) throw new Exception(uploadResult.Error.Message);
                        _logger.LogInformation("Cloudinary image URL: {Url}", uploadResult.SecureUrl?.AbsoluteUri);
                        return uploadResult.SecureUrl.AbsoluteUri;
                    }
                    else
                    {
                        var uploadParams = new RawUploadParams()
                        {
                            File = new FileDescription(file.FileName, stream)
                        };
                        var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                        if (uploadResult.Error != null) throw new Exception(uploadResult.Error.Message);
                        _logger.LogInformation("Cloudinary raw file URL: {Url}", uploadResult.SecureUrl?.AbsoluteUri);
                        return uploadResult.SecureUrl.AbsoluteUri;
                    }
                }
            }
            throw new Exception("File rỗng.");
        }

        private async Task ProcessFileMessage(ZaloDbContext dbContext, JsonElement data)
        {
            try
            {
                var senderId = data.GetProperty("sender").GetProperty("id").GetString();
                var attachments = data.GetProperty("message").GetProperty("attachments");
                var firstAttachment = attachments[0];
                var payload = firstAttachment.GetProperty("payload");
                var fileUrl = payload.GetProperty("url").GetString();
                var fileName = payload.GetProperty("name").GetString();
                var fileType = payload.GetProperty("type").GetString(); // ví dụ: "jpg", "png", "docx", ...
                var timestampStr = data.GetProperty("timestamp").GetString();
                var timestampLong = long.Parse(timestampStr);
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).UtcDateTime;

                var customer = await GetOrCreateZaloCustomerAsync(dbContext, senderId);

                // Xác định loại file là hình ảnh hay file thường
                var isImage = fileType.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                    || fileType.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                    || fileType.Equals("png", StringComparison.OrdinalIgnoreCase)
                    || fileType.Equals("gif", StringComparison.OrdinalIgnoreCase)
                    || fileType.Equals("bmp", StringComparison.OrdinalIgnoreCase)
                    || fileType.Equals("webp", StringComparison.OrdinalIgnoreCase);

                // Lưu vào DB
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

                // Phát lên SignalR, truyền thêm thông tin loại file
                await _hubContext.Clients.All.SendAsync("ReceiveZaloMessage", new
                {
                    Id = zaloMessage.Id,
                    SenderId = zaloMessage.SenderId,
                    RecipientId = zaloMessage.RecipientId,
                    Content = zaloMessage.Content,
                    Time = zaloMessage.TimeVietnam.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    Direction = zaloMessage.Direction,
                    SenderName = customer.Name,
                    SenderAvatar = customer.AvatarUrl,
                    FileName = fileName,
                    FileType = fileType,
                    IsImage = isImage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file message");
                throw;
            }
        }
        private async Task ProcessImageMessage(ZaloDbContext dbContext, JsonElement data)
        {
            try
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
                        Content = imageUrl, // Lưu URL ảnh vào Content
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
                        Time = zaloMessage.TimeVietnam.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                        Direction = zaloMessage.Direction,
                        SenderName = customer.Name,
                        SenderAvatar = customer.AvatarUrl
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing image message");
                throw;
            }
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