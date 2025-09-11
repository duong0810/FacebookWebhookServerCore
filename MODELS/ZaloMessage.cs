using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Webhook_Message.Models
{
    public class ZaloMessage
    {
        [Key]
        public int Id { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Time { get; set; } = DateTime.UtcNow;
        public string Direction { get; set; } = string.Empty; // "inbound" hoặc "outbound"

        [ForeignKey("SenderId")]
        public virtual ZaloCustomer? Sender { get; set; }

        [ForeignKey("RecipientId")]
        public virtual ZaloCustomer? Recipient { get; set; }
    }
}