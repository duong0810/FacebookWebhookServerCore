using System;

namespace Webhook_Message.Models
{
    public class Message
    {
        public int Id { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Time { get; set; }
    }

    public class MessageRequest
    {
        public string RecipientId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}