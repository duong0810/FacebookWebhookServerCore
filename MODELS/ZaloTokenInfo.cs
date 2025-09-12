namespace Webhook_Message.Models
{
    public class ZaloTokenInfo
    {
        public int Id { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpireAt { get; set; }
    }
}