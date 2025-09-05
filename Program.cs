var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection(); // Bắt buộc HTTPS cho webhook
app.UseAuthorization();
app.MapControllers();

// Cấu hình Kestrel để sử dụng HTTPS (thay đường dẫn và mật khẩu chứng chỉ)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps("path/to/your/certificate.pfx", "your-password");
    });
});

app.Run();