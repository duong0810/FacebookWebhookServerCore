using System;
using System.ComponentModel.DataAnnotations;

namespace Webhook_Message.Models
{
    public class ZaloCustomer
    {
        [Key]
        public string ZaloId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}