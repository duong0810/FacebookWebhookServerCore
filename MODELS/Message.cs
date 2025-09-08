using System;

namespace Webhook_Message.Models
{
    public class Message
    {
        public int Id { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public string Direction { get; set; } = "inbound"; // "inbound" hoặc "outbound"
    }

    public class MessageRequest
    {
        public string RecipientId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}