using BH_DataIngestionService.Infrastructure.Data;
using BH_DataIngestionService.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddWebServices();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

app.MapControllers();

app.MapGet("/", () => Results.Ok(new { service = "BH Data Ingestion Service" }));

app.Run();
