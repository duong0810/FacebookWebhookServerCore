using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Webhook_Message.Data;
using Webhook_Message.Models;

namespace Webhook_Message.Services
{
    public class ZaloAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly ZaloDbContext _dbContext;
        private readonly HttpClient _httpClient;

        public ZaloAuthService(IConfiguration configuration, ZaloDbContext dbContext, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _dbContext = dbContext;
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // Thêm timeout
        }

        public async Task<string> GetAccessTokenAsync()
        {
            try
            {
                Console.WriteLine("Bắt đầu kiểm tra token trong DB...");
                var tokenInfo = await _dbContext.ZaloTokens.FirstOrDefaultAsync();
                if (tokenInfo != null && tokenInfo.ExpireAt > DateTime.UtcNow.AddMinutes(5))
                {
                    Console.WriteLine("Token còn hạn, dùng lại token cũ.");
                    return tokenInfo.AccessToken;
                }

                string accessToken;
                if (tokenInfo != null && !string.IsNullOrEmpty(tokenInfo.RefreshToken))
                {
                    Console.WriteLine("Token hết hạn, đang refresh token...");
                    var newToken = await RefreshAccessTokenAsync(tokenInfo.RefreshToken);
                    accessToken = newToken.AccessToken;
                    tokenInfo.AccessToken = accessToken;
                    tokenInfo.RefreshToken = newToken.RefreshToken;
                    tokenInfo.ExpireAt = newToken.ExpireAt;
                    await _dbContext.SaveChangesAsync();
                    Console.WriteLine("Refresh token thành công.");
                }
                else
                {
                    var appId = _configuration["ZaloApp:AppId"];
                    var appSecret = _configuration["ZaloApp:AppSecret"];
                    if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret))
                    {
                        Console.WriteLine("Thiếu AppId hoặc AppSecret trong cấu hình.");
                        throw new Exception("Zalo AppId or AppSecret is missing in configuration.");
                    }

                    Console.WriteLine("Đang lấy token mới từ Zalo...");
                    var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("app_id", appId),
                        new KeyValuePair<string, string>("app_secret", appSecret),
                        new KeyValuePair<string, string>("grant_type", "client_credentials")
                    });

                    var response = await _httpClient.PostAsync("https://oauth.zaloapp.com/v4/oa/access_token", content);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("access_token", out var accessTokenElem))
                        throw new Exception($"Zalo API không trả về access_token. Response: {json}");

                    accessToken = accessTokenElem.GetString();
                    var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
                    var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

                    var newTokenInfo = new ZaloTokenInfo
                    {
                        AccessToken = accessToken,
                        RefreshToken = refreshToken,
                        ExpireAt = DateTime.UtcNow.AddSeconds(expiresIn - 300)
                    };
                    _dbContext.ZaloTokens.RemoveRange(_dbContext.ZaloTokens);
                    _dbContext.ZaloTokens.Add(newTokenInfo);
                    await _dbContext.SaveChangesAsync();
                    Console.WriteLine("Lưu token mới vào DB thành công.");
                }

                return accessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi khi lấy/lưu token: " + ex.Message + "\nStackTrace: " + ex.StackTrace);
                throw; // Ném exception để controller xử lý
            }
        }

        private async Task<ZaloTokenInfo> RefreshAccessTokenAsync(string refreshToken)
        {
            var oaSecret = _configuration["ZaloOA:OaSecret"];
            var appId = _configuration["ZaloApp:AppId"];

            if (string.IsNullOrEmpty(oaSecret))
            {
                throw new Exception("OA Secret is missing in configuration. Please check appsettings.json.");
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("app_id", appId),
                new KeyValuePair<string, string>("secret_key", oaSecret),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            });

            var response = await _httpClient.PostAsync("https://oauth.zaloapp.com/v4/oa/access_token", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"HTTP Error: {response.StatusCode} - Response: {errorJson}");
                throw new Exception($"Zalo API refresh failed: {errorJson}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElem) && errorElem.ValueKind != JsonValueKind.Null)
            {
                var errorName = root.TryGetProperty("error_name", out var en) ? en.GetString() : "Unknown";
                var errorDesc = root.TryGetProperty("error_description", out var ed) ? ed.GetString() : "";
                throw new Exception($"Zalo API error: {errorName} - {errorDesc}. Response: {json}");
            }

            if (!root.TryGetProperty("access_token", out var accessTokenElem))
                throw new Exception($"Zalo API không trả về access_token khi refresh. Response: {json}");

            var accessToken = accessTokenElem.GetString();
            var newRefreshToken = root.TryGetProperty("refresh_token", out var nrt) ? nrt.GetString() : null;
            var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

            return new ZaloTokenInfo
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                ExpireAt = DateTime.UtcNow.AddSeconds(expiresIn - 300)
            };
        }
    }
}