// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-005
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Health & Observability
//   version: 1.0.0
// ---

using HealthChecks.UI.Client;
using CleverSyncSOS.Infrastructure.Extensions;
using CleverSyncSOS.Infrastructure.Telemetry;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// FR-010: Structured logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container
builder.Services.AddOpenApi();

// FR-010: Add Application Insights telemetry with sanitization
builder.Services.AddApplicationInsightsTelemetry();
// Register sanitizing telemetry processor
builder.Services.AddApplicationInsightsTelemetryProcessor<SanitizingTelemetryProcessor>();

// FR-002, FR-007: Load SessionDb connection string from Azure Key Vault
var tempConfig = builder.Configuration;
builder.Configuration.AddSessionDbConnectionStringFromKeyVault(tempConfig);

// Add CleverSyncSOS services
builder.Services.AddCleverAuthentication(builder.Configuration);

// FR-005: Add health checks
builder.Services.AddCleverHealthChecks();

// CORS (allow all origins for demo - restrict in production)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseCors();

// FR-005: Expose health check endpoints
// NFR-001: Health check must respond in < 100ms
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    AllowCachingResponses = false
});

// Clever authentication-specific health check
app.MapHealthChecks("/health/clever-auth", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("clever") || check.Tags.Contains("authentication"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    AllowCachingResponses = false
});

// Liveness probe (simple ping)
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // No health checks, just returns 200 OK
    AllowCachingResponses = false
});

// Readiness probe (all checks must pass)
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    AllowCachingResponses = false
});

// Simple info endpoint
app.MapGet("/", () => new
{
    service = "CleverSyncSOS API",
    version = "1.0.0",
    stage = "Stage 3: Health & Observability",
    endpoints = new
    {
        health = "/health",
        clever_auth = "/health/clever-auth",
        liveness = "/health/live",
        readiness = "/health/ready",
        openapi = "/openapi/v1.json"
    }
})
.WithName("GetInfo")
.WithTags("Info");

app.Run();
