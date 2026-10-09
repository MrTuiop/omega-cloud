# Multi-stage Dockerfile for Omega Cloud Storage Node Daemon
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore
COPY ["Omega.StorageNode.csproj", "./"]
RUN dotnet restore "Omega.StorageNode.csproj"

# Copy source and publish
COPY . .
RUN dotnet publish "Omega.StorageNode.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5001
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Install curl for container healthcheck probe
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Create local storage volume directory
RUN mkdir -p /app/storage

COPY --from=build /app/publish .

EXPOSE 5001

HEALTHCHECK --interval=10s --timeout=5s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:5001/health || exit 1

ENTRYPOINT ["dotnet", "Omega.StorageNode.dll"]
