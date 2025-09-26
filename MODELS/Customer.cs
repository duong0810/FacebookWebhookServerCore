using System.ComponentModel.DataAnnotations;

namespace Webhook_Message.Models
{
    public class Customer
    {
        [Key]
        public string FacebookId { get; set; }

        public string Name { get; set; }

        public string AvatarUrl { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}