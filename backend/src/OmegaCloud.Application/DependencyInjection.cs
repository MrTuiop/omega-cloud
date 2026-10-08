using Microsoft.Extensions.DependencyInjection;
using OmegaCloud.Application.Services;

namespace OmegaCloud.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IFileService, FileService>();
        return services;
    }
}
