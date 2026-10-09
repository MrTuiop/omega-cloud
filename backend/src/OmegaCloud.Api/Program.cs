using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OmegaCloud.Api.Hubs;
using OmegaCloud.Api.Middleware;
using OmegaCloud.Api.Services;
using OmegaCloud.Application;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Infrastructure;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Data.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Realtime & SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

// CORS configuration for Angular client & WebSockets
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = null;
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Omega Cloud Master API",
        Version = "v1",
        Description = "Master Server API for Omega Cloud storage system."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

var app = builder.Build();

// Middleware pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Enable Swagger in all environments for ease of testing / inspection
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Omega Cloud API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("CorsPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0"
})).AllowAnonymous();

app.MapControllers();
app.MapHub<OmegaHub>(OmegaHub.HubPath);

// Apply migrations and seed data on startup with retry resilience
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    const int maxMigrationRetries = 5;
    for (int attempt = 1; attempt <= maxMigrationRetries; attempt++)
    {
        try
        {
            if (db.Database.IsRelational())
            {
                logger.LogInformation("Applying database migrations (attempt {Attempt}/{MaxRetries})...", attempt, maxMigrationRetries);
                await db.Database.MigrateAsync();
            }
            else
            {
                await db.Database.EnsureCreatedAsync();
            }

            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync();
            logger.LogInformation("Database migration and seeding completed successfully.");
            break;
        }
        catch (Exception ex) when (attempt < maxMigrationRetries)
        {
            logger.LogWarning(ex, "Database migration attempt {Attempt} failed, retrying in 2 seconds...", attempt);
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database migration/seeding was skipped or encountered an error. If PostgreSQL is not currently running, verify connection settings.");
        }
    }
}

app.Run();

// Make Program accessible for WebApplicationFactory in integration tests
public partial class Program { }
