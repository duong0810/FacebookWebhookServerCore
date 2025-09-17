using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Webhook_Message.Models
{
    public class ZaloMessage
    {
        [Key]
        public int Id { get; set; }
        public string SenderId { get; set; }
        public string RecipientId { get; set; }
        public string Content { get; set; }
        public DateTime Time { get; set; }
        public string Direction { get; set; }
        // "inbound" hoặc "outbound"

        [ForeignKey("SenderId")]
        public virtual ZaloCustomer? Sender { get; set; }
        [ForeignKey("RecipientId")]
        public virtual ZaloCustomer? Recipient { get; set; }
     
    }
}