using System;

namespace Webhook_Message.Models
{
    public class Message
    {
        public int Id { get; set; }
        public string SenderId { get; set; } = "";
        public string RecipientId { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime Time { get; set; }
        public string Direction { get; set; } = "";
    }

    public class MessageRequest
    {
        public string RecipientId { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class MessageViewModel
    {
        public int Id { get; set; }
        public string SenderId { get; set; }
        public string RecipientId { get; set; }
        public string Content { get; set; }
        public string Time { get; set; }
        public string Direction { get; set; }
    }
}