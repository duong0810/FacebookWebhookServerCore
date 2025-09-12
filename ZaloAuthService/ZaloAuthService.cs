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

        public ZaloAuthService(IConfiguration configuration, ZaloDbContext dbContext)
        {
            _configuration = configuration;
            _dbContext = dbContext;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            var tokenInfo = await _dbContext.ZaloTokens.FirstOrDefaultAsync();
            if (tokenInfo != null && tokenInfo.ExpireAt > DateTime.UtcNow.AddMinutes(1))
            {
                return tokenInfo.AccessToken;
            }

            // Nếu có refresh_token thì dùng để lấy access_token mới
            if (tokenInfo != null && !string.IsNullOrEmpty(tokenInfo.RefreshToken))
            {
                var newToken = await RefreshAccessTokenAsync(tokenInfo.RefreshToken);
                tokenInfo.AccessToken = newToken.AccessToken;
                tokenInfo.RefreshToken = newToken.RefreshToken;
                tokenInfo.ExpireAt = newToken.ExpireAt;
                await _dbContext.SaveChangesAsync();
                return newToken.AccessToken;
            }

            // Nếu chưa có token hoặc refresh_token, lấy mới bằng client_credentials
            var oaId = _configuration["ZaloOA:OaId"];
            var oaSecret = _configuration["ZaloOA:OaSecret"];

            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(new[]
            {
        new KeyValuePair<string, string>("app_id", oaId),
        new KeyValuePair<string, string>("app_secret", oaSecret),
        new KeyValuePair<string, string>("grant_type", "client_credentials")
    });

            var response = await client.PostAsync("https://oauth.zaloapp.com/v4/oa/access_token", content);
            var json = await response.Content.ReadAsStringAsync();

            var obj = JsonDocument.Parse(json).RootElement;
            if (!obj.TryGetProperty("access_token", out var accessTokenElem))
                throw new Exception($"Zalo API không trả về access_token. Response: {json}");

            var accessToken = accessTokenElem.GetString();
            var refreshToken = obj.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = obj.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

            var newTokenInfo = new ZaloTokenInfo
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpireAt = DateTime.UtcNow.AddSeconds(expiresIn - 60)
            };
            _dbContext.ZaloTokens.Add(newTokenInfo);
            await _dbContext.SaveChangesAsync();
            return accessToken;
        }

        private async Task<ZaloTokenInfo> RefreshAccessTokenAsync(string refreshToken)
        {
            var oaId = _configuration["ZaloOA:OaId"];
            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(new[]
            {
        new KeyValuePair<string, string>("app_id", oaId),
        new KeyValuePair<string, string>("grant_type", "refresh_token"),
        new KeyValuePair<string, string>("refresh_token", refreshToken)
    });

            var response = await client.PostAsync("https://oauth.zaloapp.com/v4/oa/access_token", content);
            var json = await response.Content.ReadAsStringAsync();

            var obj = JsonDocument.Parse(json).RootElement;
            if (!obj.TryGetProperty("access_token", out var accessTokenElem))
                throw new Exception($"Zalo API không trả về access_token khi refresh. Response: {json}");

            var accessToken = accessTokenElem.GetString();
            var newRefreshToken = obj.TryGetProperty("refresh_token", out var nrt) ? nrt.GetString() : null;
            var expiresIn = obj.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

            return new ZaloTokenInfo
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                ExpireAt = DateTime.UtcNow.AddSeconds(expiresIn - 60)
            };
        }
    }

}

