namespace Webhook_Message.Models
{
    public class ZaloTokenInfo
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpireAt { get; set; }
    }
}
