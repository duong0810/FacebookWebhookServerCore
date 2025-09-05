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

app.UseHttpsRedirection(); // Bắt buộc HTTPS
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

// Cấu hình để Render tự xử lý HTTPS
builder.WebHost.ConfigureKestrel(options =>
{
    // Render tự động cung cấp HTTPS, không cần cấu hình thủ công certificate
    options.ListenAnyIP(5000); // Port mặc định của Render
});

app.Run();