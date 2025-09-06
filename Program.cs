var builder = WebApplication.CreateBuilder(args);

// Cấu hình Kestrel trước khi Build
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000"));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Thêm logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();