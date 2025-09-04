using Microsoft.EntityFrameworkCore;
using Webhook_Message.Models; // Thay bằng namespace của Models

namespace Webhook_Message.Data // Thay bằng namespace phù hợp
{
    public class AppDbContext : DbContext
    {
        public DbSet<Message> Messages { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite("Data Source=messages.db");
    }
}