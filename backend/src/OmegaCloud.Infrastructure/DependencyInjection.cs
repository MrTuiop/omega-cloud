using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Data.Seeding;
using OmegaCloud.Infrastructure.Options;
using OmegaCloud.Infrastructure.Security;
using OmegaCloud.Infrastructure.Services;
using OmegaCloud.Infrastructure.Storage;
using OmegaCloud.Infrastructure.Workers;

namespace OmegaCloud.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind Jwt Options
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(jwtSection);
        var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

        // Bind Storage Options
        var storageSection = configuration.GetSection(LocalStorageOptions.SectionName);
        services.Configure<LocalStorageOptions>(storageSection);

        // Bind Background Worker Options
        var trashCleanupSection = configuration.GetSection(TrashCleanupOptions.SectionName);
        services.Configure<TrashCleanupOptions>(trashCleanupSection);

        var uploadGcSection = configuration.GetSection(UploadGcOptions.SectionName);
        services.Configure<UploadGcOptions>(uploadGcSection);

        var nodeHealthSection = configuration.GetSection(NodeHealthOptions.SectionName);
        services.Configure<NodeHealthOptions>(nodeHealthSection);

        // Database Configuration
        var useInMemory = configuration.GetValue<bool>("UseInMemoryDatabase");
        var inMemoryDbName = configuration["InMemoryDatabaseName"] ?? "OmegaCloudInMemory";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<AppDbContext>(options =>
        {
            if (useInMemory)
            {
                options.UseInMemoryDatabase(inMemoryDbName);
            }
            else if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(connectionString);
            }
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Authentication & JWT Bearer
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();

        // Default fallback Realtime Notifier
        services.TryAddSingleton<IRealtimeNotifier>(NullRealtimeNotifier.Instance);

        // Security & Time Services
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IHmacSigner, HmacSigner>();
        services.AddScoped<IJwtProvider, JwtProvider>();
        services.AddScoped<DatabaseSeeder>();

        // Storage Drivers & Gateway
        services.AddHttpClient();
        services.AddSingleton<IRemoteNodeGateway>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            var client = httpClientFactory.CreateClient("RemoteNodeGateway");
            return new RemoteNodeGateway(
                client,
                sp.GetRequiredService<IHmacSigner>(),
                sp.GetRequiredService<IDateTimeProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RemoteNodeGateway>>());
        });
        services.AddSingleton<LocalDiskStorageDriver>();
        services.AddSingleton<IStorageDriver>(sp => sp.GetRequiredService<LocalDiskStorageDriver>());
        services.AddSingleton<IStorageDriverResolver, StorageDriverResolver>();

        // Background Hosted Workers
        services.AddHostedService<TrashCleanerWorker>();
        services.AddHostedService<UploadSessionGcWorker>();
        services.AddHostedService<NodeHealthWorker>();

        return services;
    }
}
