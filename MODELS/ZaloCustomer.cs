using System.ComponentModel.DataAnnotations;

namespace Webhook_Message.Models.Zalo
{
    public class ZaloCustomer
    {
        [Key]
        public string ZaloId { get; set; } = string.Empty; // Gán giá trị mặc định

        public string Name { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
    }
}