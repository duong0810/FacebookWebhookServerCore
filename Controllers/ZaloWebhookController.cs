using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Webhook_Message.Data;
using Webhook_Message.Models.Zalo;
using Microsoft.EntityFrameworkCore;
using System;
using Microsoft.AspNetCore.SignalR;
using FacebookWebhookServerCore.Hubs;
using System.Globalization;
using Webhook_Message.Models; // Cần cho MessageViewModel

namespace FacebookWebhookServerCore.Controllers
{
    // Models để parse payload từ Zalo
    public class ZaloWebhookPayload
    {
        public string event_name { get; set; }
        public string app_id { get; set; }
        public ZaloSenderPayload sender { get; set; }
        public ZaloRecipientPayload recipient { get; set; }
        public long timestamp { get; set; }
        public ZaloMessagePayload message { get; set; }
        public string challenge { get; set; } // Dùng cho sự kiện verification
    }

    public class ZaloSenderPayload { public string id { get; set; } }
    public class ZaloRecipientPayload { public string id { get; set; } }
    public class ZaloMessagePayload { public string text { get; set; } }

    [ApiController]
    [Route("api/zalo-webhook")]
    public class ZaloWebhookController : ControllerBase
    {
        private readonly ILogger<ZaloWebhookController> _logger;
        private readonly ZaloDbContext _dbContext;
        private readonly IHubContext<ChatHub> _hubContext;

        public ZaloWebhookController(ILogger<ZaloWebhookController> logger, ZaloDbContext dbContext, IHubContext<ChatHub> hubContext)
        {
            _logger = logger;
            _dbContext = dbContext;
            _hubContext = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            string body;
            using (var reader = new StreamReader(Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }

            _logger.LogInformation("Zalo Webhook received: {Body}", body);

            try
            {
                var payload = JsonSerializer.Deserialize<ZaloWebhookPayload>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload.event_name == "verification")
                {
                    _logger.LogInformation("Zalo verification request. Challenge: {Challenge}", payload.challenge);
                    return Ok(new { challenge = payload.challenge });
                }

                if (payload.event_name == "user_send_text")
                {
                    var zaloSenderId = payload.sender.id;
                    var zaloRecipientId = payload.recipient.id; // ID của Official Account

                    var customer = await GetOrCreateZaloCustomerAsync(zaloSenderId);
                    var oa = await GetOrCreateZaloCustomerAsync(zaloRecipientId, "My Zalo OA");

                    var message = new ZaloMessage
                    {
                        SenderId = customer.ZaloId,
                        RecipientId = oa.ZaloId,
                        Content = payload.message.text,
                        Time = DateTimeOffset.FromUnixTimeMilliseconds(payload.timestamp).UtcDateTime,
                        Direction = "inbound"
                    };

                    _dbContext.ZaloMessages.Add(message);
                    await _dbContext.SaveChangesAsync();

                    // Gửi tin nhắn tới client qua SignalR
                    // Tạm thời dùng chung MessageViewModel, có thể tạo ZaloMessageViewModel nếu cần
                    var messageViewModel = new MessageViewModel
                    {
                        Id = message.Id,
                        SenderId = customer.ZaloId,
                        RecipientId = oa.ZaloId,
                        Content = message.Content,
                        Time = message.Time.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                        Direction = message.Direction,
                        SenderName = customer.Name,
                        SenderAvatar = customer.AvatarUrl,
                        Platform = "Zalo" // Thêm Platform để FE phân biệt
                    };
                    await _hubContext.Clients.All.SendAsync("ReceiveMessage", messageViewModel);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Zalo webhook");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<ZaloCustomer> GetOrCreateZaloCustomerAsync(string zaloId, string defaultName = null)
        {
            var customer = await _dbContext.ZaloCustomers.FindAsync(zaloId);
            if (customer == null)
            {
                // TODO: Gọi Zalo API để lấy thông tin người dùng nếu có thể
                customer = new ZaloCustomer
                {
                    ZaloId = zaloId,
                    Name = defaultName ?? $"Zalo User {zaloId}",
                    AvatarUrl = "", // Cần lấy từ Zalo API
                    LastUpdated = DateTime.UtcNow
                };
                _dbContext.ZaloCustomers.Add(customer);
                await _dbContext.SaveChangesAsync();
            }
            return customer;
        }
    }
}