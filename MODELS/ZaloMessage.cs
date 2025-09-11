using System.ComponentModel.DataAnnotations.Schema;

namespace Webhook_Message.Models.Zalo
{
    public class ZaloMessage
    {
        public int Id { get; set; }

        public string SenderId { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public string Direction { get; set; } = string.Empty;

        [ForeignKey("SenderId")]
        public virtual ZaloCustomer Sender { get; set; } = null!; // Thêm = null! để báo cho trình biên dịch

        [ForeignKey("RecipientId")]
        public virtual ZaloCustomer Recipient { get; set; } = null!; // Thêm = null! để báo cho trình biên dịch
    }
}