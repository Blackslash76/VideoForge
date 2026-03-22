using VideoForge.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "VideoForge API",
        Version = "v1",
        Description = "Servizio web per generare video da immagini con accelerazione GPU NVIDIA (NVENC)"
    });
});

builder.Services.AddVideoForgeInfrastructure();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500_000_000; // 500 MB
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "VideoForge API v1");
    options.RoutePrefix = string.Empty; // Swagger UI alla root
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
