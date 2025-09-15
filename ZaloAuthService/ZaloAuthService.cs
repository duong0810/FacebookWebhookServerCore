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
            _httpClient = httpClientFactory.CreateClient(); // Sử dụng IHttpClientFactory để quản lý HttpClient
        }

        public async Task<string> GetAccessTokenAsync()
        {
            var tokenInfo = await _dbContext.ZaloTokens.FirstOrDefaultAsync();
            if (tokenInfo != null && tokenInfo.ExpireAt > DateTime.UtcNow.AddMinutes(5)) // Buffer 5 phút
            {
                return tokenInfo.AccessToken;
            }

            // Refresh token nếu có
            if (tokenInfo != null && !string.IsNullOrEmpty(tokenInfo.RefreshToken))
            {
                var newToken = await RefreshAccessTokenAsync(tokenInfo.RefreshToken);
                tokenInfo.AccessToken = newToken.AccessToken;
                tokenInfo.RefreshToken = newToken.RefreshToken;
                tokenInfo.ExpireAt = newToken.ExpireAt;
                await _dbContext.SaveChangesAsync();
                return newToken.AccessToken;
            }

            // Lấy token mới bằng app_id và app_secret
            var appId = _configuration["ZaloApp:AppId"]; // Sửa config key
            var appSecret = _configuration["ZaloApp:AppSecret"]; // Sửa config key

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret))
            {
                throw new Exception("Zalo AppId or AppSecret is missing in configuration.");
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("app_id", appId),
                new KeyValuePair<string, string>("app_secret", appSecret),
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _httpClient.PostAsync("https://oauth.zaloapp.com/v4/oa/access_token", content);
            response.EnsureSuccessStatusCode(); // Ném exception nếu không thành công
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var accessTokenElem))
                throw new Exception($"Zalo API không trả về access_token. Response: {json}");

            var accessToken = accessTokenElem.GetString();
            var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

            var newTokenInfo = new ZaloTokenInfo
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpireAt = DateTime.UtcNow.AddSeconds(expiresIn - 300) // Buffer 5 phút
            };
            _dbContext.ZaloTokens.RemoveRange(_dbContext.ZaloTokens); // Xóa token cũ (nếu cần)
            _dbContext.ZaloTokens.Add(newTokenInfo);
            await _dbContext.SaveChangesAsync();

            return accessToken;
        }

        private async Task<ZaloTokenInfo> RefreshAccessTokenAsync(string refreshToken)
        {
            var appId = _configuration["ZaloApp:AppId"];

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("app_id", appId),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            });

            var response = await _httpClient.PostAsync("https://oauth.zaloapp.com/v4/oa/access_token", content);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
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