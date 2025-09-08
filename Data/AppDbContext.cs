using Microsoft.EntityFrameworkCore;
using Webhook_Message.Models;

namespace Webhook_Message.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Message> Messages { get; set; }
    }
}