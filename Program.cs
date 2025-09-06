var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

<<<<<<< HEAD
// Thêm logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole(); // Ghi log ra console (Render sẽ hiển thị)
    logging.AddDebug();  // Tùy chọn, nếu cần debug cục bộ
=======
// Đặt cấu hình Kestrel ở đây, trước khi build app
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000"));
>>>>>>> edf9e186ef26d4a3328a3f907289256cbcde6e3d
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

app.Run();