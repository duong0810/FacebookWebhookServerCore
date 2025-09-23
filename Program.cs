using Webhook_Message.Data;
using Microsoft.EntityFrameworkCore;
using FacebookWebhookServerCore.Hubs;
using Webhook_Message.Models;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Http.Features;
using System.IO;
using Webhook_Message.Services;

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

// Thêm CORS để cho phép các request từ Zalo
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
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

// Đăng ký DbContext cho Facebook (PostgreSQL)
builder.Services.AddDbContext<FBDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Đăng ký DbContext cho Zalo (PostgreSQL)
builder.Services.AddDbContext<ZaloDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ZaloAuthService>();

var app = builder.Build();

// Cấu hình middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Thứ tự middleware quan trọng
app.UseStaticFiles(); // Phục vụ file xác thực Zalo

// Thêm CORS middleware
app.UseCors();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

// Áp dụng migrations khi khởi động ứng dụng
using (var scope = app.Services.CreateScope())
{
    // Áp dụng migration cho DB của Facebook
    var facebookDb = scope.ServiceProvider.GetRequiredService<FBDbContext>();
    //facebookDb.Database.Migrate();

    // Áp dụng migration cho DB của Zalo
    var zaloDb = scope.ServiceProvider.GetRequiredService<ZaloDbContext>();
    //zaloDb.Database.Migrate();

    // Khởi tạo token Zalo từ appsettings.json nếu chưa có trong DB
    var zaloAuthService = scope.ServiceProvider.GetRequiredService<ZaloAuthService>();
    zaloAuthService.EnsureTokenInitializedAsync().GetAwaiter().GetResult();
}

// Kiểm tra thư mục wwwroot
var webRoot = app.Environment.WebRootPath;
Console.WriteLine($"WebRootPath: {webRoot}");
if (Directory.Exists(webRoot))
{
    var files = Directory.GetFiles(webRoot, "*", SearchOption.AllDirectories);
    Console.WriteLine($"Files in wwwroot: {string.Join(", ", files)}");
}
else
{
    Console.WriteLine("WebRootPath directory does not exist!");
}

app.Run();