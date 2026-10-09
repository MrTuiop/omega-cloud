# Multi-stage Dockerfile for Omega Cloud Master Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for layer caching
COPY ["OmegaCloud.sln", "./"]
COPY ["src/OmegaCloud.Domain/OmegaCloud.Domain.csproj", "src/OmegaCloud.Domain/"]
COPY ["src/OmegaCloud.Application/OmegaCloud.Application.csproj", "src/OmegaCloud.Application/"]
COPY ["src/OmegaCloud.Infrastructure/OmegaCloud.Infrastructure.csproj", "src/OmegaCloud.Infrastructure/"]
COPY ["src/OmegaCloud.Api/OmegaCloud.Api.csproj", "src/OmegaCloud.Api/"]
COPY ["tests/OmegaCloud.Tests/OmegaCloud.Tests.csproj", "tests/OmegaCloud.Tests/"]

# Restore packages
RUN dotnet restore "src/OmegaCloud.Api/OmegaCloud.Api.csproj"

# Copy full source and publish
COPY . .
WORKDIR /src/src/OmegaCloud.Api
RUN dotnet publish "OmegaCloud.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Configure ASP.NET Core environment and URLs
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Install curl for container healthcheck probe
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Create local storage volume directory
RUN mkdir -p /app/storage

COPY --from=build /app/publish .

EXPOSE 5000

HEALTHCHECK --interval=10s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "OmegaCloud.Api.dll"]
