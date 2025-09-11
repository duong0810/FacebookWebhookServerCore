using Webhook_Message.Data;
using Microsoft.EntityFrameworkCore;
using FacebookWebhookServerCore.Hubs;
using Webhook_Message.Models;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Kestrel để xử lý các request lớn
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000"));
    options.Limits.MaxRequestBodySize = 30 * 1024 * 1024; // 30 MB
});

// Cấu hình giới hạn cho form data
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 30 * 1024 * 1024; // 30 MB
});

// Đăng ký các dịch vụ cơ bản
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Cấu hình logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});

// Cấu hình và đăng ký Cloudinary
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));
builder.Services.AddSingleton(sp =>
{
    var account = new Account(
        builder.Configuration["CloudinarySettings:CloudName"],
        builder.Configuration["CloudinarySettings:ApiKey"],
        builder.Configuration["CloudinarySettings:ApiSecret"]);
    return new Cloudinary(account);
});

// Đăng ký DbContext cho Facebook
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Đăng ký DbContext cho Zalo
// Chú ý: Cần thêm connection string "ZaloConnection" vào appsettings.json
builder.Services.AddDbContext<ZaloDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ZaloConnection") ??
                     "Data Source=zalo.db"));

var app = builder.Build();

// Cấu hình middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

// Áp dụng migrations khi khởi động ứng dụng
using (var scope = app.Services.CreateScope())
{
    // Áp dụng migration cho DB của Facebook
    var facebookDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    facebookDb.Database.Migrate();

    // Áp dụng migration cho DB của Zalo
    var zaloDb = scope.ServiceProvider.GetRequiredService<ZaloDbContext>();
    zaloDb.Database.Migrate();
}

app.Run();