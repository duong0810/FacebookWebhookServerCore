using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

[ApiController]
[Route("api/[controller]")]
public class SendMessageController : ControllerBase
{
    private readonly ILogger<SendMessageController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _pageAccessToken = "YOUR_PAGE_ACCESS_TOKEN"; // Thay bằng token thực tế, tốt nhất lưu trong biến môi trường

    public SendMessageController(ILogger<SendMessageController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.MessageText))
        {
            return BadRequest("UserId and MessageText are required.");
        }

        var payload = new
        {
            messaging_type = "RESPONSE", // Sử dụng "MESSAGE_TAG" nếu ngoài 24 giờ với tag hợp lệ
            recipient = new { id = request.UserId },
            message = new { text = request.MessageText }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var url = $"https://graph.facebook.com/v20.0/me/messages?access_token={_pageAccessToken}";
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.PostAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Message sent successfully to {UserId}", request.UserId);
            return Ok(new { status = "success" });
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to send message to {UserId}: {Error}", request.UserId, error);
            return StatusCode((int)response.StatusCode, new { error = error });
        }
    }
}

public class SendMessageRequest
{
    public string UserId { get; set; }
    public string MessageText { get; set; }
}