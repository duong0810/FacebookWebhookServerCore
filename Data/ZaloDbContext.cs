using Microsoft.EntityFrameworkCore;
using Webhook_Message.Models;

namespace Webhook_Message.Data
{
    public class ZaloDbContext : DbContext
    {
        public ZaloDbContext(DbContextOptions<ZaloDbContext> options) : base(options) { }
        public DbSet<ZaloMessage> ZaloMessages { get; set; }
        public DbSet<ZaloCustomer> ZaloCustomers { get; set; }
        public DbSet<ZaloTokenInfo> ZaloTokens { get; set; }
    }
}