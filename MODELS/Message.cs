using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Webhook_Message.Models
{
    public class Message
    {
        public int Id { get; set; }

        // Foreign Key đến bảng Customer
        public string SenderId { get; set; } = string.Empty;

        public string RecipientId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public string Direction { get; set; } = "inbound";

        // Navigation property để liên kết với Customer
        [ForeignKey("SenderId")]
        public virtual Customer Sender { get; set; }
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
        public string SenderName { get; set; }
        public string SenderAvatar { get; set; }
        public string Platform { get; set; } = string.Empty;

    }
}