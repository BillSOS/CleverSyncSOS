// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-020
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Azure Functions
//   version: 1.0.0
// ---

using CleverSyncSOS.Infrastructure.Extensions;
using CleverSyncSOS.Infrastructure.Telemetry;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// FR-010: Add Application Insights telemetry with sanitization
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();
// Register sanitizing telemetry processor
builder.Services.AddSingleton<ITelemetryProcessor, SanitizingTelemetryProcessor>();

// FR-002, FR-007: Load SessionDb connection string from Azure Key Vault
var tempConfig = builder.Configuration;
builder.Configuration.AddSessionDbConnectionStringFromKeyVault(tempConfig);

// Add CleverSyncSOS services
// FR-001, FR-002, FR-003: Authentication services
builder.Services.AddCleverAuthentication(builder.Configuration);

// FR-012: Clever API Client
builder.Services.AddCleverApiClient(builder.Configuration);

// FR-020: Sync services
builder.Services.AddCleverSync(builder.Configuration);

// FR-005: Health checks (optional for Functions, but useful)
builder.Services.AddCleverHealthChecks();

builder.Build().Run();
