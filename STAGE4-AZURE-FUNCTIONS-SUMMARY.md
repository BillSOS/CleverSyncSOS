# Stage 4: Azure Functions - Implementation Summary

**Completed**: 2025-11-13
**Status**: ✅ **COMPLETE AND READY TO DEPLOY**

---

## Overview

Stage 4 implements Azure Functions for scheduled and manual synchronization of student and teacher data from Clever API to your databases. This provides a serverless, scalable solution for automated sync operations.

---

## What Was Implemented

### 1. Azure Functions Project

**Location**: `src/CleverSyncSOS.Functions/`

**Technology Stack**:
- .NET 9.0
- Azure Functions V4 (isolated worker process)
- Application Insights integration
- Full dependency injection support

### 2. Timer-Triggered Function (`SyncTimerFunction`)

**Purpose**: Automatic daily synchronization

**Configuration**:
- **Schedule**: Daily at 2 AM UTC (`0 0 2 * * *`)
- **Trigger**: Timer (cron expression)
- **Scope**: Syncs all districts and schools

**Features**:
- Orchestrates district-level sync
- Logs sync summary (schools succeeded/failed, records processed)
- Automatic retry via Azure Functions infrastructure
- Application Insights telemetry

**Code**: `src/CleverSyncSOS.Functions/SyncTimerFunction.cs`

### 3. HTTP-Triggered Manual Sync Function (`ManualSyncFunction`)

**Purpose**: On-demand synchronization via HTTP POST

**Endpoint**: `POST /api/sync`

**Query Parameters**:

| Parameter | Type | Description | Example |
|-----------|------|-------------|---------|
| `schoolId` | int | Sync specific school | `?schoolId=3` |
| `districtId` | int | Sync all schools in district | `?districtId=1` |
| `forceFullSync` | bool | Force full sync (not incremental) | `?schoolId=3&forceFullSync=true` |
| *(none)* | - | Sync all districts | *no parameters* |

**Authorization**: Function-level (Azure Functions Key required)

**Response Format**:

```json
{
  "success": true,
  "scope": "school",
  "schoolId": 3,
  "schoolName": "City High School",
  "syncType": "Full",
  "duration": 58.92,
  "statistics": {
    "studentsProcessed": 1488,
    "studentsFailed": 0,
    "studentsDeleted": 0,
    "teachersProcessed": 82,
    "teachersFailed": 0,
    "teachersDeleted": 0
  },
  "timestamp": "2025-11-13T23:30:00.000Z"
}
```

**Code**: `src/CleverSyncSOS.Functions/ManualSyncFunction.cs`

### 4. Dependency Injection Configuration

**Program.cs** fully configured with:
- CleverSyncSOS authentication services
- Clever API client
- Sync orchestration services
- SessionDb and SchoolDb contexts
- Application Insights telemetry
- Health checks

All services from Stages 1-3 are available via DI.

### 5. Configuration Files

#### `local.settings.json` (for local testing)
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "CleverAuth__KeyVaultUri": "https://cleversyncsos.vault.azure.net/",
    "CleverAuth__TokenEndpoint": "https://clever.com/oauth/tokens",
    "CleverAuth__MaxRetryAttempts": "5",
    "CleverAuth__InitialRetryDelaySeconds": "2",
    "CleverAuth__TokenRefreshThresholdPercent": "75.0",
    "CleverAuth__HttpTimeoutSeconds": "30",
    "CleverApi__BaseUrl": "https://api.clever.com/v3.0",
    "CleverApi__PageSize": "100"
  }
}
```

#### `host.json` (Function runtime settings)
- Configured for optimized performance
- Logging and Application Insights integration

---

## Features

### Scheduled Sync (Timer Function)

✅ **Automatic daily execution** at 2 AM UTC
✅ **Syncs all districts** and their schools
✅ **Parallel school processing** (max 5 concurrent)
✅ **Comprehensive logging** of sync results
✅ **Retry logic** for transient failures
✅ **Application Insights** telemetry

### Manual Sync (HTTP Function)

✅ **On-demand sync** via HTTP POST
✅ **Granular control**: School, district, or full sync
✅ **Force full sync** option
✅ **Detailed JSON responses** with statistics
✅ **Function-level authorization** (requires function key)
✅ **RESTful API design**

### Orchestration

✅ **District-level sync** processes all schools in parallel
✅ **School-level sync** for targeted updates
✅ **Error isolation**: One school failure doesn't stop others
✅ **Sync history tracking** in SessionDb
✅ **Incremental and full sync** support

---

## Local Testing

### Prerequisites

1. **Azure Storage Emulator** or **Azurite** (for Azure Functions runtime)
2. **Azure Key Vault** access (with Clever credentials)
3. **SQL Server** (LocalDB or Azure SQL)

### Run Locally

```bash
# Install Azure Functions Core Tools (if not installed)
npm install -g azure-functions-core-tools@4 --unsafe-perm true

# Navigate to Functions project
cd src/CleverSyncSOS.Functions

# Run locally
func start
```

### Test Manual Sync Endpoint

```bash
# Sync specific school (replace {key} with your function key)
curl -X POST "http://localhost:7071/api/sync?schoolId=3&forceFullSync=true" \
  -H "x-functions-key: {key}"

# Sync all districts
curl -X POST "http://localhost:7071/api/sync" \
  -H "x-functions-key: {key}"
```

---

## Deployment to Azure

### Option 1: Azure CLI

```bash
# Login to Azure
az login

# Create resource group (if needed)
az group create --name CleverSyncSOS-RG --location eastus

# Create storage account
az storage account create \
  --name cleversyncsosfunc \
  --resource-group CleverSyncSOS-RG \
  --sku Standard_LRS

# Create Function App
az functionapp create \
  --name CleverSyncSOS-Functions \
  --resource-group CleverSyncSOS-RG \
  --storage-account cleversyncsosfunc \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --runtime-version 9.0 \
  --functions-version 4

# Deploy
func azure functionapp publish CleverSyncSOS-Functions
```

### Option 2: Visual Studio 2026

1. Right-click on `CleverSyncSOS.Functions` project
2. Select **Publish**
3. Choose **Azure** → **Azure Function App (Windows/Linux)**
4. Select or create Function App
5. Click **Publish**

### Option 3: GitHub Actions / Azure DevOps

Deploy via CI/CD pipeline (recommended for production).

---

## Configuration in Azure

After deployment, configure Application Settings in Azure Portal:

```
CleverAuth__KeyVaultUri = https://cleversyncsos.vault.azure.net/
CleverAuth__TokenEndpoint = https://clever.com/oauth/tokens
CleverAuth__MaxRetryAttempts = 5
CleverAuth__InitialRetryDelaySeconds = 2
CleverAuth__TokenRefreshThresholdPercent = 75.0
CleverAuth__HttpTimeoutSeconds = 30
CleverApi__BaseUrl = https://api.clever.com/v3.0
CleverApi__PageSize = 100
CleverApi__MaxRetryAttempts = 5
CleverApi__InitialRetryDelaySeconds = 2
APPLICATIONINSIGHTS_CONNECTION_STRING = {your-app-insights-connection-string}
```

### Managed Identity Setup

1. Enable **System-assigned Managed Identity** on the Function App
2. Grant the managed identity **Key Vault Secrets User** role on your Key Vault
3. Update Key Vault access policies if using older auth model

---

## Monitoring

### Application Insights

All functions are instrumented with Application Insights:
- **Invocation count** and success rate
- **Duration** metrics
- **Custom metrics**: Records processed, schools synced
- **Exceptions** and errors
- **Dependency tracking**: Key Vault, SQL, Clever API

### Queries

**Sync success rate over last 24 hours**:
```kusto
traces
| where timestamp > ago(24h)
| where message contains "Manual sync completed"
| summarize count() by bin(timestamp, 1h)
```

**Failed syncs**:
```kusto
traces
| where severityLevel >= 3 // Error or Critical
| where message contains "sync failed"
| project timestamp, message, customDimensions
```

---

## Authorization

### Function Keys

By default, functions use **Function-level authorization**:
- Each function has its own key
- Master (host) key available for all functions
- Keys managed in Azure Portal → Function App → App Keys

### Retrieve Function Key

```bash
az functionapp function keys list \
  --name CleverSyncSOS-Functions \
  --resource-group CleverSyncSOS-RG \
  --function-name ManualSync
```

### Call Function with Key

```bash
curl -X POST \
  "https://cleversyncsos-functions.azurewebsites.net/api/sync?schoolId=3" \
  -H "x-functions-key: YOUR_FUNCTION_KEY"
```

---

## Files Created

| File | Purpose |
|------|---------|
| `CleverSyncSOS.Functions.csproj` | Project file with .NET 9.0 and Azure Functions packages |
| `Program.cs` | DI configuration, service registration |
| `SyncTimerFunction.cs` | Timer-triggered daily sync |
| `ManualSyncFunction.cs` | HTTP-triggered manual sync |
| `local.settings.json` | Local development configuration |
| `host.json` | Azure Functions runtime settings |
| `.gitignore` | Excludes local settings from source control |

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│ Azure Functions (Serverless)                             │
├─────────────────────────────────────────────────────────┤
│                                                           │
│  ┌──────────────────────┐  ┌──────────────────────┐    │
│  │ SyncTimerFunction    │  │ ManualSyncFunction   │    │
│  │ (Timer: 2 AM UTC)    │  │ (HTTP POST)          │    │
│  └──────────┬───────────┘  └──────────┬───────────┘    │
│             │                           │                │
│             └───────────┬───────────────┘                │
│                         │                                │
│                  ┌──────▼──────┐                        │
│                  │ SyncService │                        │
│                  └──────┬──────┘                        │
│                         │                                │
│         ┌───────────────┼───────────────┐              │
│         │               │               │              │
│  ┌──────▼──────┐ ┌─────▼──────┐ ┌──────▼──────┐      │
│  │  District 1  │ │ District 2  │ │ District 3  │      │
│  │  Sync        │ │ Sync        │ │ Sync        │      │
│  └──────┬───────┘ └─────┬──────┘ └──────┬──────┘      │
│         │               │               │              │
│    ┌────▼────┐     ┌───▼───┐     ┌────▼────┐         │
│    │ School 1│     │School │     │ School N│         │
│    │ School 2│     │   M   │     │         │         │
│    │ School 3│     └───────┘     └─────────┘         │
│    └─────────┘                                         │
│                                                         │
└─────────────────────────────────────────────────────────┘
                         │
        ┌────────────────┼────────────────┐
        │                │                │
  ┌─────▼──────┐  ┌──────▼──────┐  ┌─────▼──────┐
  │ Clever API │  │ Azure Key   │  │  SQL       │
  │            │  │ Vault       │  │  Databases │
  └────────────┘  └─────────────┘  └────────────┘
```

---

## Success Criteria

### FR-020: Sync Orchestration ✅

| Requirement | Status |
|-------------|--------|
| Timer-triggered Azure Function (daily at 2 AM UTC) | ✅ DONE |
| Manual trigger via HTTP endpoint | ✅ DONE |
| Support school-level sync via `?schoolId={id}` | ✅ DONE |
| Support district-level sync via `?districtId={id}` | ✅ DONE |
| Support full sync with no parameters | ✅ DONE |
| Log start/end timestamps and record counts | ✅ DONE |
| Function-level authorization | ✅ DONE |
| Return sync status in HTTP response | ✅ DONE |

---

## Next Steps

1. **Deploy to Azure**:
   - Create Azure Function App
   - Configure Application Settings
   - Enable Managed Identity
   - Deploy functions

2. **Set Up Monitoring**:
   - Configure Application Insights alerts
   - Create dashboards for sync metrics
   - Set up email notifications for failures

3. **Testing in Azure**:
   - Test timer function (wait for scheduled run or trigger manually)
   - Test manual sync endpoint with Postman/curl
   - Verify sync history in SessionDb

4. **Production Readiness**:
   - Review and update timer schedule if needed
   - Configure appropriate timeout values
   - Set up backup and disaster recovery
   - Document operational procedures

---

## Summary

✅ **Stage 4 is COMPLETE!**

We've successfully implemented:
- Timer-triggered function for automated daily sync
- HTTP-triggered function for manual on-demand sync
- Full dependency injection with all CleverSyncSOS services
- Application Insights integration
- Function-level authorization
- Comprehensive error handling and logging

**Production Ready**: The Functions are ready to deploy to Azure and begin automated synchronization! 🚀

---

## Commands Reference

```bash
# Build Functions project
dotnet build src/CleverSyncSOS.Functions

# Run locally
cd src/CleverSyncSOS.Functions && func start

# Test manual sync
curl -X POST "http://localhost:7071/api/sync?schoolId=3" \
  -H "x-functions-key: default"

# Deploy to Azure
func azure functionapp publish CleverSyncSOS-Functions

# View logs in real-time
func azure functionapp logstream CleverSyncSOS-Functions
```
