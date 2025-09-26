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
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
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
                    try
                    {
                        var newToken = await RefreshAccessTokenAsync(tokenInfo.RefreshToken);
                        accessToken = newToken.AccessToken;
                        tokenInfo.AccessToken = accessToken;
                        tokenInfo.RefreshToken = newToken.RefreshToken;
                        tokenInfo.ExpireAt = newToken.ExpireAt.ToUniversalTime();
                        await _dbContext.SaveChangesAsync();
                        Console.WriteLine("Refresh token thành công.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Refresh token thất bại, xóa token cũ khỏi DB. Lỗi: " + ex.Message);
                        _dbContext.ZaloTokens.Remove(tokenInfo);
                        await _dbContext.SaveChangesAsync();
                        throw;
                    }
                }
                else
                {
                    var refreshTokenConfig = _configuration["ZaloApp:RefreshToken"];
                    if (string.IsNullOrEmpty(refreshTokenConfig))
                    {
                        Console.WriteLine("Thiếu RefreshToken trong ZaloApp section của appsettings.json.");
                        throw new Exception("Thiếu RefreshToken trong ZaloApp section của appsettings.json.");
                    }

                    Console.WriteLine("Token DB chưa có, đang refresh token từ cấu hình ZaloApp...");
                    var newToken = await RefreshAccessTokenAsync(refreshTokenConfig);
                    accessToken = newToken.AccessToken;
                    var newTokenInfo = new ZaloTokenInfo
                    {
                        AccessToken = accessToken,
                        RefreshToken = newToken.RefreshToken,
                        ExpireAt = newToken.ExpireAt.ToUniversalTime()
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
                throw;
            }
        }

        private async Task<ZaloTokenInfo> RefreshAccessTokenAsync(string refreshToken)
        {
            var appId = _configuration["ZaloApp:AppId"];
            var appSecret = _configuration["ZaloApp:AppSecret"];

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret))
                throw new Exception("AppId hoặc AppSecret bị thiếu trong cấu hình.");

            var client = _httpClient;
            // Đảm bảo không bị trùng header
            if (client.DefaultRequestHeaders.Contains("secret_key"))
                client.DefaultRequestHeaders.Remove("secret_key");
            client.DefaultRequestHeaders.Add("secret_key", appSecret);

            var data = new[]
            {
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("app_id", appId),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
            };

            var response = await client.PostAsync(
                "https://oauth.zaloapp.com/v4/oa/access_token",
                new FormUrlEncodedContent(data)
            );
            var json = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[Zalo Refresh] Request: refresh_token={refreshToken}, app_id={appId}");
            Console.WriteLine($"[Zalo Refresh] Response: {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Chỉ throw nếu error khác 0
            if (root.TryGetProperty("error", out var errorElem) && errorElem.GetInt32() != 0)
            {
                var errorName = root.TryGetProperty("error_name", out var en) ? en.GetString() : "Unknown";
                var errorDesc = root.TryGetProperty("error_description", out var ed) ? ed.GetString() : "";
                throw new Exception($"Zalo API error: {errorName} - {errorDesc}. Response: {json}");
            }

            if (!root.TryGetProperty("access_token", out var accessTokenElem))
                throw new Exception($"Zalo API không trả về access_token khi refresh. Response: {json}");

            var accessToken = accessTokenElem.GetString();
            var newRefreshToken = root.TryGetProperty("refresh_token", out var nrt) && !string.IsNullOrEmpty(nrt.GetString()) ? nrt.GetString() : refreshToken;

            int expiresIn = 3600;
            if (root.TryGetProperty("expires_in", out var ei))
            {
                if (ei.ValueKind == JsonValueKind.String)
                {
                    int.TryParse(ei.GetString(), out expiresIn);
                }
                else if (ei.ValueKind == JsonValueKind.Number)
                {
                    expiresIn = ei.GetInt32();
                }
            }

            return new ZaloTokenInfo
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                ExpireAt = DateTime.UtcNow.AddSeconds(expiresIn - 300)
            };
        }

        public async Task EnsureTokenInitializedAsync()
        {
            var tokenInfo = await _dbContext.ZaloTokens.FirstOrDefaultAsync();
            if (tokenInfo == null)
            {
                var refreshToken = _configuration["ZaloApp:RefreshToken"];
                if (string.IsNullOrEmpty(refreshToken))
                {
                    Console.WriteLine("Thiếu RefreshToken trong ZaloApp section của appsettings.json.");
                    return;
                }

                var newToken = await RefreshAccessTokenAsync(refreshToken);
                _dbContext.ZaloTokens.Add(new ZaloTokenInfo
                {
                    AccessToken = newToken.AccessToken,
                    RefreshToken = newToken.RefreshToken,
                    ExpireAt = newToken.ExpireAt
                });
                await _dbContext.SaveChangesAsync();
                Console.WriteLine("Khởi tạo token từ cấu hình appsettings.json thành công.");
            }
        }

        public async Task<ZaloTokenInfo> RefreshAccessTokenPublicAsync(string refreshToken)
        {
            return await RefreshAccessTokenAsync(refreshToken);
        }
    }
}