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
        public string Status { get; set; } // "sent", "received", "seen"
        public DateTime? StatusTime { get; set; } // Thời điểm cập nhật trạng thái

        [ForeignKey("SenderId")]
        public virtual ZaloCustomer? Sender { get; set; }
        [ForeignKey("RecipientId")]
        public virtual ZaloCustomer? Recipient { get; set; }
        public string? MsgId { get; set; }
        [NotMapped]
        public DateTime TimeVietnam
        {
            get
            {
                var vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(Time, vietnamTimeZone);
            }
        }
    }
}