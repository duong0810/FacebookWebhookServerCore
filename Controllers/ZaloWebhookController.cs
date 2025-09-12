using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using FacebookWebhookServerCore.Hubs;
using Webhook_Message.Data;
using Webhook_Message.Models;
using System.Globalization;
using System.Net.Http;

namespace FacebookWebhookServerCore.Controllers
{
    [ApiController]
    [Route("api/zalo-webhook")]
    public class ZaloWebhookController : ControllerBase
    {
        private readonly ILogger<ZaloWebhookController> _logger;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _oaId; // Cần thay thế với ID OA thật
        private readonly string _oaSecret; // Cần thay thế với Secret thật
        private readonly string _accessToken; // Access token của Zalo OA

        public ZaloWebhookController(
            ILogger<ZaloWebhookController> logger,
            IHubContext<ChatHub> hubContext,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _hubContext = hubContext;
            _httpClientFactory = httpClientFactory;
            _oaId = configuration["ZaloOA:OaId"];
            _oaSecret = configuration["ZaloOA:OaSecret"];
            _accessToken = configuration["ZaloOA:AccessToken"];
        }

        // Endpoint để xác thực domain với Zalo
        [HttpGet]
        public IActionResult Get()
        {
            try
            {
                _logger.LogInformation("Received Zalo domain verification request");

                // Ghi log tất cả các query parameters để debug
                foreach (var param in Request.Query)
                {
                    _logger.LogInformation("Query parameter: {Key} = {Value}", param.Key, param.Value);
                }

                // Trả về 200 OK để xác nhận domain
                return Ok("Zalo webhook verification successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Zalo domain verification");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Endpoint để nhận webhook events từ Zalo
        [HttpPost]
        public async Task<IActionResult> Post([FromServices] ZaloDbContext dbContext)
        {
            try
            {
                // Đọc nội dung request
                string requestBody;
                using (var reader = new StreamReader(Request.Body))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                _logger.LogInformation("Received Zalo webhook: {Body}", requestBody);

                // Xác thực chữ ký từ Zalo (nếu có)
                if (Request.Headers.TryGetValue("X-ZaloOA-Signature", out var signature))
                {
                    if (!VerifySignature(requestBody, signature))
                    {
                        _logger.LogWarning("Invalid Zalo webhook signature");
                        return Unauthorized("Invalid signature");
                    }
                }

                // Parse dữ liệu JSON
                using var jsonDoc = JsonDocument.Parse(requestBody);
                var root = jsonDoc.RootElement;

                // Kiểm tra xem có phải là event tin nhắn không
                if (root.TryGetProperty("event_name", out var eventNameElement) &&
                    eventNameElement.GetString() == "user_send_text")
                {
                    // Xử lý tin nhắn văn bản từ user
                    await ProcessTextMessage(dbContext, root);
                }
                // Thêm xử lý cho các loại event khác sau này

                return Ok(new { status = "success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Zalo webhook");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Hàm xử lý tin nhắn văn bản
        private async Task ProcessTextMessage(ZaloDbContext dbContext, JsonElement data)
        {
            try
            {
                var sender = data.GetProperty("sender").GetProperty("id").GetString();
                var message = data.GetProperty("message").GetProperty("text").GetString();
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(
                    data.GetProperty("timestamp").GetInt64()).UtcDateTime;

                // Kiểm tra hoặc tạo ZaloCustomer
                var customer = await GetOrCreateZaloCustomerAsync(dbContext, sender);

                // Lưu tin nhắn vào database
                var zaloMessage = new ZaloMessage
                {
                    SenderId = sender,
                    RecipientId = _oaId,
                    Content = message,
                    Time = timestamp,
                    Direction = "inbound"
                };

                dbContext.ZaloMessages.Add(zaloMessage);
                await dbContext.SaveChangesAsync();

                // Thông báo qua SignalR
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing text message");
                throw;
            }
        }

        // API để gửi tin nhắn từ ứng dụng của bạn
        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromServices] ZaloDbContext dbContext, [FromBody] ZaloMessageRequest request)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"https://openapi.zalo.me/v2.0/oa/message";

                var payload = new
                {
                    recipient = new { user_id = request.RecipientId },
                    message = new { text = request.Message }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );

                client.DefaultRequestHeaders.Add("access_token", _accessToken);
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    // Lưu tin nhắn đã gửi vào database
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

                    // Thông báo qua SignalR
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

                    var responseContent = await response.Content.ReadAsStringAsync();
                    return Ok(new { status = "success", details = responseContent });
                }

                var errorResponse = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, new { status = "error", details = errorResponse });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to Zalo");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Lấy hoặc tạo thông tin ZaloCustomer
        private async Task<ZaloCustomer> GetOrCreateZaloCustomerAsync(ZaloDbContext dbContext, string userId)
        {
            var customer = await dbContext.ZaloCustomers.FindAsync(userId);

            if (customer == null || customer.LastUpdated < DateTime.UtcNow.AddDays(-1))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var url = $"https://openapi.zalo.me/v2.0/oa/getprofile?data={{'user_id':'{userId}'}}";

                    client.DefaultRequestHeaders.Add("access_token", _accessToken);
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
                    _logger.LogError(ex, "Error fetching Zalo user profile");
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

        // Đảm bảo OA của bạn tồn tại trong database
        private async Task<ZaloCustomer> EnsureOACustomerExistsAsync(ZaloDbContext dbContext)
        {
            var oaCustomer = await dbContext.ZaloCustomers.FindAsync(_oaId);
            if (oaCustomer == null)
            {
                oaCustomer = new ZaloCustomer
                {
                    ZaloId = _oaId,
                    Name = "Zalo OA", // Tên của OA
                    AvatarUrl = "", // URL Avatar của OA
                    LastUpdated = DateTime.UtcNow
                };
                dbContext.ZaloCustomers.Add(oaCustomer);
                await dbContext.SaveChangesAsync();
            }
            return oaCustomer;
        }

        // Hàm xác thực chữ ký từ Zalo
        private bool VerifySignature(string payload, string signature)
        {
            try
            {
                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_oaSecret)))
                {
                    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                    var computedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();
                    return computedSignature == signature.ToString().ToLower();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying Zalo signature");
                return false;
            }
        }
    }

    public class ZaloMessageRequest
    {
        public string RecipientId { get; set; }
        public string Message { get; set; }
    }
}