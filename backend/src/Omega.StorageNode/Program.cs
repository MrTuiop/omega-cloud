using Microsoft.AspNetCore.Http.Features;
using Omega.StorageNode.Configuration;
using Omega.StorageNode.Endpoints;
using Omega.StorageNode.Services;
using Omega.StorageNode.Workers;

var builder = WebApplication.CreateBuilder(args);

// 1. Remove Kestrel body size limits for streaming large chunks and files
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null;
});

// 2. Options configuration with environment variable fallbacks
builder.Services.Configure<StorageNodeOptions>(options =>
{
    builder.Configuration.GetSection(StorageNodeOptions.SectionName).Bind(options);

    // Support conventional environment variables
    var masterUrlEnv = Environment.GetEnvironmentVariable("MASTER_BASE_URL");
    if (!string.IsNullOrWhiteSpace(masterUrlEnv)) options.MasterBaseUrl = masterUrlEnv;

    var regKeyEnv = Environment.GetEnvironmentVariable("NODE_REGISTRATION_KEY");
    if (!string.IsNullOrWhiteSpace(regKeyEnv)) options.RegistrationKey = regKeyEnv;

    var nodeIdEnv = Environment.GetEnvironmentVariable("NODE_ID");
    if (!string.IsNullOrWhiteSpace(nodeIdEnv) && Guid.TryParse(nodeIdEnv, out var parsedNodeId)) options.NodeId = parsedNodeId;

    var nodeSecretEnv = Environment.GetEnvironmentVariable("NODE_SECRET");
    if (!string.IsNullOrWhiteSpace(nodeSecretEnv)) options.NodeSecret = nodeSecretEnv;

    var nodeNameEnv = Environment.GetEnvironmentVariable("NODE_NAME");
    if (!string.IsNullOrWhiteSpace(nodeNameEnv)) options.NodeName = nodeNameEnv;

    var baseUrlEnv = Environment.GetEnvironmentVariable("NODE_BASE_URL");
    if (!string.IsNullOrWhiteSpace(baseUrlEnv)) options.BaseUrl = baseUrlEnv;

    var isBehindNatEnv = Environment.GetEnvironmentVariable("NODE_IS_BEHIND_NAT");
    if (!string.IsNullOrWhiteSpace(isBehindNatEnv) && bool.TryParse(isBehindNatEnv, out var parsedNat)) options.IsBehindNat = parsedNat;

    var storagePathEnv = Environment.GetEnvironmentVariable("STORAGE_PATH");
    if (!string.IsNullOrWhiteSpace(storagePathEnv)) options.StorageRootPath = storagePathEnv;
});

// 3. CORS configuration for browser direct access
builder.Services.AddCors(cors =>
{
    cors.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("Content-Disposition", "Content-Length", "ETag", "X-Chunk-SHA256");
    });
});

// 4. Core Services
builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
builder.Services.AddSingleton<INodeStateManager, NodeStateManager>();
builder.Services.AddSingleton<INodeHmacValidator, NodeHmacValidator>();
builder.Services.AddSingleton<IStorageService, StorageService>();

// 5. Workers
builder.Services.AddHttpClient<NodeLifecycleWorker>();
builder.Services.AddHostedService<NodeLifecycleWorker>();
builder.Services.AddHostedService<StorageGcWorker>();

// 6. OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Remove body size limits in pipeline
app.Use(async (context, next) =>
{
    var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (maxRequestBodySizeFeature is not null && !maxRequestBodySizeFeature.IsReadOnly)
    {
        maxRequestBodySizeFeature.MaxRequestBodySize = null;
    }
    await next();
});

// Map Minimal API Endpoints
app.MapStorageNodeEndpoints();

app.Run();

public partial class Program { }
