using Microsoft.EntityFrameworkCore;
using Webhook_Message.Models;

namespace Webhook_Message.Data
{
    public class ZaloDbContext : DbContext
    {
        public ZaloDbContext(DbContextOptions<ZaloDbContext> options) : base(options)
        {
        }

        public DbSet<ZaloCustomer> ZaloCustomers { get; set; }
        public DbSet<ZaloMessage> ZaloMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cấu hình quan hệ giữa ZaloMessage và ZaloCustomer
            modelBuilder.Entity<ZaloMessage>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ZaloMessage>()
                .HasOne(m => m.Recipient)
                .WithMany()
                .HasForeignKey(m => m.RecipientId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}