var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Đặt cấu hình Kestrel ở đây, trước khi build app
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Loại bỏ app.UseHttpsRedirection() vì Render tự xử lý HTTPS
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();