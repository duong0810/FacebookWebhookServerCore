using Microsoft.EntityFrameworkCore;
using Webhook_Message.Models;

namespace Webhook_Message.Data
{
    public class FBDbContext : DbContext
    {
        public FBDbContext(DbContextOptions<FBDbContext> options) : base(options) { }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Customer> Customers { get; set; } 
    }
}