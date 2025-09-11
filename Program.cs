//using Webhook_Message.Data;
//using Microsoft.EntityFrameworkCore;
//using FacebookWebhookServerCore.Hubs;
//using Webhook_Message.Models;
//using CloudinaryDotNet;
//using Microsoft.AspNetCore.Http.Features; // Thêm using này

//var builder = WebApplication.CreateBuilder(args);

//// Cấu hình Kestrel trước khi Build
//builder.WebHost.ConfigureKestrel(options =>
//{
//    options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000"));
//    // Tăng giới hạn kích thước request body của Kestrel
//    options.Limits.MaxRequestBodySize = 30 * 1024 * 1024; // 30 MB
//});

//// Cấu hình giới hạn cho form data
//builder.Services.Configure<FormOptions>(options =>
//{
//    options.MultipartBodyLengthLimit = 30 * 1024 * 1024; // 30 MB
//});


//builder.Services.AddControllers();
//builder.Services.AddSignalR();
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
//builder.Services.AddHttpClient();

//// Thêm logging
//builder.Services.AddLogging(logging =>
//{
//    logging.AddConsole();
//    logging.AddDebug();
//});

//// Cấu hình và đăng ký Cloudinary
//builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));
//builder.Services.AddSingleton(sp =>
//{
//    var account = new Account(
//        builder.Configuration["CloudinarySettings:CloudName"],
//        builder.Configuration["CloudinarySettings:ApiKey"],
//        builder.Configuration["CloudinarySettings:ApiSecret"]);
//    return new Cloudinary(account);
//});


//builder.Services.AddDbContext<AppDbContext>(options =>
//    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

//// Đăng ký DbContext riêng cho Zalo
//builder.Services.AddDbContext<ZaloDbContext>(options =>
//    options.UseSqlite(builder.Configuration.GetConnectionString("ZaloConnection")));

//var app = builder.Build();

//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

//app.UseStaticFiles();
//app.UseRouting();
//app.UseAuthorization();
//app.MapControllers();
//app.MapHub<ChatHub>("/chatHub");

//using (var scope = app.Services.CreateScope())
//{
//    // Áp dụng migration cho DB của Facebook
//    var facebookDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//    facebookDb.Database.Migrate();

//    // Áp dụng migration cho DB của Zalo
//    var zaloDb = scope.ServiceProvider.GetRequiredService<ZaloDbContext>();
//    zaloDb.Database.Migrate();
//}
//app.Run();

using Webhook_Message.Data;
using Microsoft.EntityFrameworkCore;
using FacebookWebhookServerCore.Hubs;
using Webhook_Message.Models;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Http.Features; // Thêm using này

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Kestrel trước khi Build
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000"));
    // Tăng giới hạn kích thước request body của Kestrel
    options.Limits.MaxRequestBodySize = 30 * 1024 * 1024; // 30 MB
});

// Cấu hình giới hạn cho form data
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 30 * 1024 * 1024; // 30 MB
});


builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Thêm logging
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


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Đăng ký DbContext riêng cho Zalo
builder.Services.AddDbContext<ZaloDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ZaloConnection")));

var app = builder.Build();

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