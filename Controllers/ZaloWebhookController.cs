using FacebookWebhookServerCore.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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

        public ZaloWebhookController(
            ILogger<ZaloWebhookController> logger,
            IHubContext<ChatHub> hubContext,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ZaloAuthService zaloAuthService)
        {
            _logger = logger;
            _hubContext = hubContext;
            _httpClientFactory = httpClientFactory;
            _oaId = configuration["ZaloOA:OaId"];
            _oaSecret = configuration["ZaloOA:OaSecret"];
            _zaloAuthService = zaloAuthService;
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
                .ToListAsync();

            return Ok(messages);
        }

        [HttpGet("messages/customer/{customerId}")]
        public async Task<IActionResult> GetMessagesByCustomerId([FromServices] ZaloDbContext dbContext, string customerId)
        {
            var messages = await dbContext.ZaloMessages
                .Where(m => m.SenderId == customerId || m.RecipientId == customerId)
                .OrderByDescending(m => m.Time)
                .ToListAsync();

            return Ok(messages);
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

                if (root.TryGetProperty("event_name", out var eventNameElement) &&
                    eventNameElement.GetString() == "user_send_text")
                {
                    await ProcessTextMessage(dbContext, root);
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
                var sender = data.GetProperty("sender").GetProperty("id").GetString();
                var message = data.GetProperty("message").GetProperty("text").GetString();
                var timestampStr = data.GetProperty("timestamp").GetString();
                var timestampLong = long.Parse(timestampStr);
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampLong).UtcDateTime;

                var customer = await GetOrCreateZaloCustomerAsync(dbContext, sender);
                var oaCustomer = await EnsureOACustomerExistsAsync(dbContext);

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

                var accessToken = await _zaloAuthService.GetAccessTokenAsync();
                client.DefaultRequestHeaders.Add("access_token", accessToken);
                var response = await client.PostAsync(url, content);

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

        private async Task<ZaloCustomer> GetOrCreateZaloCustomerAsync(ZaloDbContext dbContext, string userId)
        {
            var customer = await dbContext.ZaloCustomers.FindAsync(userId);

            if (customer == null || customer.LastUpdated < DateTime.UtcNow.AddDays(-1))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var url = $"https://openapi.zalo.me/v2.0/oa/getprofile?data={{'user_id':'{userId}'}}";

                    var accessToken = await _zaloAuthService.GetAccessTokenAsync();
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

        private async Task<ZaloCustomer> EnsureOACustomerExistsAsync(ZaloDbContext dbContext)
        {
            var oaCustomer = await dbContext.ZaloCustomers.FindAsync(_oaId);
            if (oaCustomer == null)
            {
                oaCustomer = new ZaloCustomer
                {
                    ZaloId = _oaId,
                    Name = "Zalo OA",
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
}