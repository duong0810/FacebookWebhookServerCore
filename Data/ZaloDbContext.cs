using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Webhook_Message.Models.Zalo;

namespace Webhook_Message.Data
{
    public class ZaloDbContext : DbContext // <--- SỬA LỖI QUAN TRỌNG Ở ĐÂY
    {
        public ZaloDbContext(DbContextOptions<ZaloDbContext> options) : base(options)
        {
        }

        public DbSet<ZaloMessage> ZaloMessages { get; set; }
        public DbSet<ZaloCustomer> ZaloCustomers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Đặt tên bảng để tránh xung đột nếu dùng chung DB
            modelBuilder.Entity<ZaloCustomer>().ToTable("ZaloCustomers");
            modelBuilder.Entity<ZaloMessage>().ToTable("ZaloMessages");

            // Cấu hình mối quan hệ
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

    // Lớp Factory này vẫn giữ nguyên, nó rất hữu ích cho các lệnh migration sau này
    public class ZaloDbContextFactory : IDesignTimeDbContextFactory<ZaloDbContext>
    {
        public ZaloDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ZaloDbContext>();
            optionsBuilder.UseSqlite("Data Source=/tmp/zalo_webhook_message.db");

            return new ZaloDbContext(optionsBuilder.Options);
        }
    }
}