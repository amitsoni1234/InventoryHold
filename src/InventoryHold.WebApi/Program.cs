using InventoryHold.Contracts;
using InventoryHold.Infrastructure.DependencyInjection;
using InventoryHold.WebApi.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddInventoryInfrastructure(builder.Configuration);
builder.Services.AddHoldApplication(builder.Configuration);

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();
if (origins is null || origins.Length == 0)
{
    var raw = builder.Configuration["Cors:Origins"];
    origins = string.IsNullOrWhiteSpace(raw)
        ? ["http://localhost:5173"]
        : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled");
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        logger.LogError(exception, "Unhandled exception.");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ApiError
        {
            ErrorCode = "INTERNAL_ERROR",
            Message = "An unexpected error occurred."
        });
    });
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("frontend");
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
