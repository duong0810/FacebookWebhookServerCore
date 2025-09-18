namespace Webhook_Message.Models
{
    public class ZaloMessageRequest
    {
        public string RecipientId { get; set; } // Dùng cho flow tư vấn OA
        public string Message { get; set; }
        public string AnonymousId { get; set; } // Dùng cho flow ẩn danh
        public string ConversationId { get; set; } // Dùng cho flow ẩn danh
    }
}