var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Thêm logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole(); // Ghi log ra console (Render sẽ hiển thị)
    logging.AddDebug();  // Tùy chọn, nếu cần debug cục bộ
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Loại bỏ UseHttpsRedirection nếu Render tự xử lý HTTPS
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000"));
});

app.Run();