using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Data.Seeding;
using OmegaCloud.Infrastructure.Security;
using OmegaCloud.Infrastructure.Services;
using OmegaCloud.Infrastructure.Storage;

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
        });

        services.AddAuthorization();

        // Security & Time Services
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<IJwtProvider, JwtProvider>();
        services.AddScoped<DatabaseSeeder>();

        // Storage Drivers
        services.AddSingleton<LocalDiskStorageDriver>();
        services.AddSingleton<IStorageDriver>(sp => sp.GetRequiredService<LocalDiskStorageDriver>());
        services.AddSingleton<IStorageDriverResolver, StorageDriverResolver>();

        return services;
    }
}
