using System.Net;
using System.Text.RegularExpressions;

namespace OmegaCloud.Tests.Integration;

public class DeploymentConfigurationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly string _repoRoot;

    public DeploymentConfigurationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        // Locate repo root relative to backend project
        var currentDir = AppContext.BaseDirectory;
        var dirInfo = new DirectoryInfo(currentDir);
        while (dirInfo != null && !File.Exists(Path.Combine(dirInfo.FullName, "docker-compose.yml")))
        {
            dirInfo = dirInfo.Parent;
        }

        _repoRoot = dirInfo?.FullName ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    }

    [Fact]
    public async Task HealthEndpoint_WithoutToken_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body);
    }

    [Fact]
    public void DockerComposeFile_ExistsAndDefinesRequiredServices()
    {
        var composePath = Path.Combine(_repoRoot, "docker-compose.yml");
        Assert.True(File.Exists(composePath), $"docker-compose.yml not found at {composePath}");

        var content = File.ReadAllText(composePath);

        // Required services
        Assert.Contains("postgres:", content);
        Assert.Contains("backend:", content);
        Assert.Contains("frontend:", content);
        Assert.Contains("caddy:", content);

        // Network
        Assert.Contains("omega-internal", content);

        // Ports and dependencies
        Assert.Contains("5432", content);
        Assert.Contains("5000", content);
        Assert.Contains("80:80", content);
        Assert.Contains("443:443", content);
        Assert.Contains("depends_on:", content);
    }

    [Fact]
    public void EnvExampleFile_ExistsAndDefinesRequiredVariables()
    {
        var envPath = Path.Combine(_repoRoot, ".env.example");
        Assert.True(File.Exists(envPath), $".env.example not found at {envPath}");

        var content = File.ReadAllText(envPath);

        var requiredVars = new[]
        {
            "DOMAIN=",
            "CADDY_TLS=",
            "POSTGRES_DB=",
            "POSTGRES_USER=",
            "POSTGRES_PASSWORD=",
            "POSTGRES_PORT=",
            "ASPNETCORE_ENVIRONMENT=",
            "JWT_SECRET=",
            "JWT_ISSUER=",
            "JWT_AUDIENCE=",
            "JWT_ACCESS_MINUTES=",
            "JWT_REFRESH_DAYS=",
            "LOCAL_STORAGE_PATH=",
            "POSTGRES_DATA_PATH=",
            "CADDY_DATA_PATH="
        };

        foreach (var reqVar in requiredVars)
        {
            Assert.True(content.Contains(reqVar), $"Missing variable {reqVar} in .env.example");
        }
    }

    [Fact]
    public void Caddyfile_ExistsAndConfiguresProxyAndSecurity()
    {
        var caddyPath = Path.Combine(_repoRoot, "Caddyfile");
        Assert.True(File.Exists(caddyPath), $"Caddyfile not found at {caddyPath}");

        var content = File.ReadAllText(caddyPath);

        Assert.Contains("backend:5000", content);
        Assert.Contains("handle /api/*", content);
        Assert.Contains("handle /hubs/*", content);
        Assert.Contains("handle /swagger/*", content);
        Assert.Contains("/srv/frontend", content);
        Assert.Contains("file_server", content);
        Assert.Contains("try_files", content);
        Assert.Contains("Strict-Transport-Security", content);
        Assert.Contains("X-Content-Type-Options", content);
        Assert.Contains("X-Frame-Options", content);
        Assert.Contains("encode zstd gzip", content);
    }

    [Fact]
    public void BackendDockerfile_ExistsAndIsMultiStage()
    {
        var dockerfilePath = Path.Combine(_repoRoot, "backend", "Dockerfile");
        Assert.True(File.Exists(dockerfilePath), $"backend/Dockerfile not found at {dockerfilePath}");

        var content = File.ReadAllText(dockerfilePath);

        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build", content);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime", content);
        Assert.Contains("dotnet publish", content);
        Assert.Contains("EXPOSE 5000", content);
        Assert.Contains("ENTRYPOINT", content);
    }

    [Fact]
    public void FrontendDockerfile_ExistsAndIsMultiStage()
    {
        var dockerfilePath = Path.Combine(_repoRoot, "frontend", "Dockerfile");
        Assert.True(File.Exists(dockerfilePath), $"frontend/Dockerfile not found at {dockerfilePath}");

        var content = File.ReadAllText(dockerfilePath);

        Assert.Contains("FROM node:22-alpine AS build", content);
        Assert.Contains("npm run build", content);
        Assert.Contains("FROM alpine:3.20", content);
        Assert.Contains("EXPOSE 80", content);
        Assert.Contains("HEALTHCHECK", content);
    }

    [Fact]
    public void DockerignoreFiles_ExistForBothComponents()
    {
        var backendDockerignore = Path.Combine(_repoRoot, "backend", ".dockerignore");
        var frontendDockerignore = Path.Combine(_repoRoot, "frontend", ".dockerignore");

        Assert.True(File.Exists(backendDockerignore), "backend/.dockerignore not found");
        Assert.True(File.Exists(frontendDockerignore), "frontend/.dockerignore not found");

        var backendContent = File.ReadAllText(backendDockerignore);
        Assert.Contains("bin/", backendContent);
        Assert.Contains("obj/", backendContent);
        Assert.Contains("**/bin/", backendContent);
        Assert.Contains("**/obj/", backendContent);

        var frontendContent = File.ReadAllText(frontendDockerignore);
        Assert.Contains("node_modules/", frontendContent);
        Assert.Contains("dist/", frontendContent);
        Assert.Contains("**/node_modules/", frontendContent);
    }

    [Fact]
    public void DockerCompose_ConfiguresServiceHealthchecksAndDependencies()
    {
        var composePath = Path.Combine(_repoRoot, "docker-compose.yml");
        Assert.True(File.Exists(composePath));
        var content = File.ReadAllText(composePath);

        // All services have healthcheck definitions
        Assert.Contains("pg_isready", content);
        Assert.Contains("curl", content);
        Assert.Contains("wget", content);

        // Caddy depends on healthy backend and frontend
        Assert.Contains("condition: service_healthy", content);
    }

    [Fact]
    public void Caddyfile_ConfiguresUnbufferedStreamingAndIndexNoCache()
    {
        var caddyPath = Path.Combine(_repoRoot, "Caddyfile");
        Assert.True(File.Exists(caddyPath));
        var content = File.ReadAllText(caddyPath);

        Assert.Contains("flush_interval -1", content);
        Assert.Contains("no-cache, no-store, must-revalidate", content);
        Assert.Contains("redir /swagger /swagger/", content);
    }
}
