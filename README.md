# CleverSyncSOS

**Version:** 1.0.0
**Phase:** Stage 1 - Core Implementation
**Framework:** .NET 9.0

## Overview

CleverSyncSOS enables secure, automated synchronization between school and district SIS data via Clever's API and individual school Azure SQL databases. This solution implements OAuth 2.0 authentication, token management, and robust retry logic to ensure reliable synchronization across school systems.

### Current Implementation (Stage 1)

This release implements **Clever API Authentication and Connection** with the following features:

- ✅ OAuth 2.0 client credentials flow
- ✅ Azure Key Vault credential storage with managed identity
- ✅ In-memory token caching with proactive refresh (75% lifetime threshold)
- ✅ Exponential backoff retry logic (5 attempts)
- ✅ Rate limiting detection and handling (HTTP 429)
- ✅ Structured logging with Azure Application Insights integration
- ✅ TLS 1.2+ enforcement
- ✅ Comprehensive unit and integration tests

### Upcoming Stages

- **Stage 2**: Clever-to-Azure Database Sync (data fetching and synchronization)
- **Stage 3**: Health Check Endpoints (monitoring and observability)

## Source Documents

This implementation is derived from:

- **Constitution**: `SpecKit/Constitution/constitution.md` (v1.1.0)
- **Specification**: `SpecKit/Specs/001-clever-api-auth/spec-1.md` (v1.0.0)
- **Plan**: `SpecKit/Plans/001-clever-api-auth/plan.md` (v1.0.0)

See `speckit.yaml` for complete traceability.

## Prerequisites

- **.NET 9 SDK** or later
- **Visual Studio 2022** (17.8+) or **Visual Studio Code** with C# Dev Kit
- **Azure Subscription** with:
  - Azure Key Vault
  - Managed Identity enabled on the hosting environment
  - Application Insights (optional, for telemetry)
- **Clever API Credentials** (Client ID and Client Secret)

## Solution Structure

```
CleverSyncSOS.sln
├── src/
│   ├── CleverSyncSOS.Core/              # Core business logic and authentication
│   │   ├── Authentication/              # OAuth service and credential store
│   │   ├── Configuration/               # Configuration models and token classes
│   │   └── Health/                      # Health check models (Stage 3)
│   ├── CleverSyncSOS.Infrastructure/    # DI extensions and HTTP configuration
│   │   └── Extensions/                  # Service registration extensions
│   └── CleverSyncSOS.Console/           # Console application entry point
├── tests/
│   ├── CleverSyncSOS.Core.Tests/        # Unit tests for core logic
│   └── CleverSyncSOS.Integration.Tests/ # Integration tests
├── SpecKit/                             # Source specifications and plans
├── README.md                            # This file
└── speckit.yaml                         # Traceability manifest
```

## Configuration

### 1. Azure Key Vault Setup

Store your Clever API credentials in Azure Key Vault:

```bash
# Create Key Vault (if needed)
az keyvault create --name <your-keyvault-name> --resource-group <your-rg> --location <location>

# Store Clever credentials
az keyvault secret set --vault-name <your-keyvault-name> --name CleverClientId --value <your-client-id>
az keyvault secret set --vault-name <your-keyvault-name> --name CleverClientSecret --value <your-client-secret>

# Grant access to managed identity (for Azure App Service or Functions)
az keyvault set-policy --name <your-keyvault-name> --object-id <managed-identity-object-id> --secret-permissions get list
```

### 2. Application Configuration

Update `appsettings.json` in `src/CleverSyncSOS.Console/`:

```json
{
  "CleverAuth": {
    "KeyVaultUri": "https://<your-keyvault-name>.vault.azure.net/",
    "ClientIdSecretName": "CleverClientId",
    "ClientSecretSecretName": "CleverClientSecret",
    "TokenEndpoint": "https://clever.com/oauth/tokens",
    "ApiBaseUrl": "https://api.clever.com",
    "TokenRefreshThresholdPercent": 75.0,
    "MaxRetryAttempts": 5,
    "InitialRetryDelaySeconds": 2,
    "RequestTimeoutSeconds": 30,
    "ConnectionTimeoutSeconds": 10
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "CleverSyncSOS": "Debug"
    }
  }
}
```

### 3. Environment Variables (Optional)

Override configuration using environment variables with the `CLEVERSYNC_` prefix:

```bash
# Windows
set CLEVERSYNC_CleverAuth__KeyVaultUri=https://your-vault.vault.azure.net/
set CLEVERSYNC_CleverAuth__MaxRetryAttempts=3

# Linux/macOS
export CLEVERSYNC_CleverAuth__KeyVaultUri=https://your-vault.vault.azure.net/
export CLEVERSYNC_CleverAuth__MaxRetryAttempts=3
```

## Build Instructions

### Using Visual Studio 2022+

1. Open `CleverSyncSOS.sln` in Visual Studio
2. Build the solution: **Build → Build Solution** (Ctrl+Shift+B)
3. Run tests: **Test → Run All Tests** (Ctrl+R, A)

### Using .NET CLI

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build --configuration Release

# Run tests
dotnet test

# Build specific project
dotnet build src/CleverSyncSOS.Console/CleverSyncSOS.Console.csproj
```

## Run Instructions

### Console Application

```bash
# Run from solution root
dotnet run --project src/CleverSyncSOS.Console/CleverSyncSOS.Console.csproj

# Or from Console project directory
cd src/CleverSyncSOS.Console
dotnet run
```

### Expected Output

```
==============================================
CleverSyncSOS - Clever API Authentication Demo
==============================================

Authenticating with Clever API...
✓ Authentication successful!

Token Information:
  Token Type: Bearer
  Expires In: 3600 seconds
  Issued At: 2025-11-10 15:30:45 UTC
  Expires At: 2025-11-10 16:30:45 UTC
  Time Until Expiration: 01:00:00
  Should Refresh (75% threshold): False

Retrieving token from cache...
✓ Retrieved token from cache (no re-authentication needed)

Health Status:
  Last Successful Auth: 2025-11-10 15:30:45 UTC
  Last Error: None

Stage 1 (Core Implementation) demonstration complete!
Next steps: Stage 2 (Database Sync) and Stage 3 (Health Endpoints)
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity detailed

# Run specific test project
dotnet test tests/CleverSyncSOS.Core.Tests/CleverSyncSOS.Core.Tests.csproj

# Run tests with coverage (requires coverlet)
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

## Development Standards

This project follows the CleverSyncSOS Constitution principles:

- ✅ **Security First**: Credentials stored in Azure Key Vault with managed identity
- ✅ **Dependency Injection**: All services registered via DI container
- ✅ **Async Patterns**: Async/await throughout for scalability
- ✅ **Structured Logging**: ILogger with contextual properties
- ✅ **Configuration**: Externalized via appsettings.json and environment variables
- ✅ **Testing**: Comprehensive unit and integration test coverage
- ✅ **Code Quality**: Nullable reference types enabled, follows .NET conventions

## Troubleshooting

### Common Issues

**1. Key Vault Access Denied**
- Ensure managed identity has proper Key Vault access policy
- Verify Key Vault URI is correct in configuration
- Check that secrets exist with the correct names

**2. Authentication Fails with 401**
- Verify Clever API credentials are correct
- Check that credentials in Key Vault are up-to-date
- Ensure Clever API endpoint is accessible

**3. Rate Limiting (HTTP 429)**
- The system automatically retries with exponential backoff
- Check logs for retry attempts and delays
- Consider adjusting `MaxRetryAttempts` and `InitialRetryDelaySeconds`

**4. Build Errors**
- Ensure .NET 9 SDK is installed: `dotnet --version`
- Clean and rebuild: `dotnet clean && dotnet build`
- Restore packages: `dotnet restore`

### Logging

Enable detailed logging by updating `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "CleverSyncSOS": "Trace",
      "System.Net.Http.HttpClient": "Debug"
    }
  }
}
```

## Deployment

### Azure App Service / Azure Functions

1. Publish the Console project as a self-contained deployment
2. Configure managed identity on the App Service/Function
3. Set application settings via Azure Portal or CLI
4. Deploy using Visual Studio Publish, Azure DevOps, or GitHub Actions

### CI/CD Pipeline

See `SpecKit/Plans/001-clever-api-auth/plan.md` for CI/CD roadmap details.

## License

Copyright © 2025 Bill Martin. All rights reserved.

## Support

For issues or questions, contact the repository owner: **Bill Martin**

## Next Steps

- **Stage 2**: Implement data fetching from Clever API and sync to Azure SQL
- **Stage 3**: Add health check endpoints and monitoring infrastructure
- Configure CI/CD pipeline for automated deployment
- Enable Application Insights for production telemetry

---

**Generated with SpecKit** | **Version 1.0.0** | **Phase: Stage 1 - Core Implementation**
