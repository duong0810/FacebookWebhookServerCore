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
                    // Lấy từ cấu hình OA nếu chưa có trong DB
                    var accessTokenConfig = _configuration["ZaloOA:AccessToken"];
                    var refreshTokenConfig = _configuration["ZaloOA:RefreshToken"];
                    var expireAtStr = _configuration["ZaloOA:ExpiredTime"];
                    DateTime expireAtConfig = DateTime.TryParse(expireAtStr, out var dt) ? dt : DateTime.UtcNow.AddHours(1);

                    if (!string.IsNullOrEmpty(accessTokenConfig) && expireAtConfig > DateTime.UtcNow.AddMinutes(5))
                    {
                        Console.WriteLine("Dùng access token từ cấu hình OA.");
                        // Lưu vào DB nếu chưa có
                        if (tokenInfo == null)
                        {
                            _dbContext.ZaloTokens.Add(new ZaloTokenInfo
                            {
                                AccessToken = accessTokenConfig,
                                RefreshToken = refreshTokenConfig,
                                ExpireAt = expireAtConfig
                            });
                            await _dbContext.SaveChangesAsync();
                        }
                        return accessTokenConfig;
                    }

                    // Nếu token cấu hình cũng hết hạn, refresh bằng refresh token OA
                    Console.WriteLine("Token cấu hình hết hạn, đang refresh token OA...");
                    var newToken = await RefreshAccessTokenAsync(refreshTokenConfig);
                    accessToken = newToken.AccessToken;
                    var newTokenInfo = new ZaloTokenInfo
                    {
                        AccessToken = accessToken,
                        RefreshToken = newToken.RefreshToken,
                        ExpireAt = newToken.ExpireAt
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
            var oaSecret = _configuration["ZaloOA:OaSecret"];
            var oaId = _configuration["ZaloOA:OaId"];

            if (string.IsNullOrEmpty(oaSecret) || string.IsNullOrEmpty(oaId))
                throw new Exception("OA Secret hoặc OA Id bị thiếu trong cấu hình. Vui lòng kiểm tra appsettings.json.");

            var content = new FormUrlEncodedContent(new[]
            {
        new KeyValuePair<string, string>("app_id", oaId),
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

        public async Task EnsureTokenInitializedAsync()
        {
            var tokenInfo = await _dbContext.ZaloTokens.FirstOrDefaultAsync();
            if (tokenInfo == null)
            {
                var accessToken = _configuration["ZaloOA:AccessToken"];
                var refreshToken = _configuration["ZaloOA:RefreshToken"];
                var expireAtStr = _configuration["ZaloOA:ExpiredTime"];
                DateTime expireAt = DateTime.TryParse(expireAtStr, out var dt) ? dt : DateTime.UtcNow.AddHours(1);

                if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
                {
                    _dbContext.ZaloTokens.Add(new ZaloTokenInfo
                    {
                        AccessToken = accessToken,
                        RefreshToken = refreshToken,
                        ExpireAt = expireAt
                    });
                    await _dbContext.SaveChangesAsync();
                    Console.WriteLine("Khởi tạo token từ cấu hình appsettings.json thành công.");
                }
                else
                {
                    Console.WriteLine("Thiếu AccessToken hoặc RefreshToken trong appsettings.json.");
                }
            }
        }

        public async Task<ZaloTokenInfo> RefreshAccessTokenPublicAsync(string refreshToken)
        {
            return await RefreshAccessTokenAsync(refreshToken);
        }
    }
}