var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Cấu hình Kestrel trước khi build (nếu cần tùy chỉnh port hoặc HTTPS)
builder.WebHost.ConfigureKestrel(options =>
{
    // Đặt port (Render tự động gán port, nên để mặc định hoặc dùng biến môi trường)
    options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000"));
    // Nếu cần HTTPS thủ công (Render tự cung cấp HTTPS, nên bỏ qua phần này)
    // options.ListenAnyIP(5000, listenOptions => listenOptions.UseHttps("path/to/cert.pfx", "password"));
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection(); // Render tự cung cấp HTTPS, giữ middleware này
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();