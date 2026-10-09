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
        services.AddScoped<IUploadService, UploadService>();
        services.AddScoped<ITrashService, TrashService>();
        services.AddScoped<IUploadGcService, UploadGcService>();
        services.AddScoped<INodeService, NodeService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<ISystemHealthService, SystemHealthService>();
        return services;
    }
}
