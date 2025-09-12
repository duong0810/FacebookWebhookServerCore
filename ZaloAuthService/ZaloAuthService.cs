using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

public class ZaloAuthService
{
    private readonly IConfiguration _configuration;

    public ZaloAuthService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<string> GetAccessTokenAsync()
    {
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
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        var obj = System.Text.Json.JsonDocument.Parse(json);
        return obj.RootElement.GetProperty("access_token").GetString();
    }
}