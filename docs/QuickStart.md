# Quick Start Guide

**Version:** 2.0.0
**Last Updated:** 2025-11-25

---

## 🎯 Choose Your Path

### Option 1: Admin Portal (Recommended for Most Users)

**For a graphical interface**, deploy and use the CleverSync Admin Portal:

**Production URL:** https://cleversyncsos.azurewebsites.net

📖 **See:** [Admin Portal Quick Start Guide](AdminPortal-QuickStart.md)

The Admin Portal provides:
- ✅ Web-based configuration and management
- ✅ No command-line experience required
- ✅ User-friendly interface for all operations
- ✅ Built-in role-based access control

---

### Option 2: CLI Setup (This Guide)

**For automation, scripting, or advanced users**, follow the manual CLI steps below.

## Immediate Manual Steps Required

Before you can run CleverSyncSOS via CLI, complete these steps:

---

## 1. Store SessionDb Connection String in Key Vault

**Action**: Store your SessionDb connection string securely in Azure Key Vault:

```bash
az keyvault secret set \
  --vault-name cleversync-kv \
  --name CleverSyncSOS--SessionDb--ConnectionString \
  --value "Server=tcp:cleversync-sql-prod.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SessionDbOwner;Password=YOUR_PASSWORD;Encrypt=True;"
```

**Note:** For Admin Portal deployments, you'll also need the `Session-PW` secret (just the password) for constructing connection strings.

Replace `YOUR_PASSWORD` with your actual SQL Server password.

---

## 2. Login to Azure CLI

This allows the app to access Azure Key Vault:

```bash
az login
```

Verify you're logged in:
```bash
az account show
```

---

## 3. Apply SessionDb Migration

Create the SessionDb schema:

```bash
cd src/CleverSyncSOS.Core

dotnet ef database update --context SessionDbContext --connection "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

Replace `YOUR_PASSWORD` with your actual password.

---

## 4. Add Your District to SessionDb

Connect to your SessionDb database (using Azure Data Studio, SSMS, or Azure Portal Query Editor) and run:

```sql
USE SessionDb;

-- Insert your district
INSERT INTO Districts (CleverDistrictId, Name, DistrictPrefix, CreatedAt, UpdatedAt)
VALUES
('YOUR_CLEVER_DISTRICT_ID', 'Your District Name', 'YourDistrict-District', GETUTCDATE(), GETUTCDATE());

-- Check it was inserted
SELECT * FROM Districts;
```

---

## 5. Add Your School(s) to SessionDb

```sql
USE SessionDb;

-- Get your district's CleverDistrictId
DECLARE @DistrictId NVARCHAR(50) = (SELECT CleverDistrictId FROM Districts WHERE Name = 'Your District Name');

-- Insert your school
INSERT INTO Schools (
    DistrictId,
    CleverSchoolId,
    Name,
    DatabaseName,
    SchoolPrefix,
    IsActive,
    RequiresFullSync,
    CreatedAt,
    UpdatedAt
)
VALUES
(
    @DistrictId,  -- Uses CleverDistrictId (nvarchar)
    'YOUR_CLEVER_SCHOOL_ID',
    'Your School Name',
    'School_YourSchoolName',  -- Database name (create this next)
    'YourSchool-Elementary',  -- SchoolPrefix (used for Key Vault secret naming)
    1,  -- IsActive
    1,  -- RequiresFullSync (for first sync)
    GETUTCDATE(),
    GETUTCDATE()
);

-- Check it was inserted
SELECT * FROM Schools;
```

---

## 6. Create Per-School Database

Create a database for each school you added:

```bash
# Example: Create database for your school
az sql db create \
  --resource-group your-resource-group \
  --server sos-northcentral \
  --name School_YourSchoolName \
  --service-objective S0
```

**Or** use Azure Portal:
1. Go to your SQL Server: `sos-northcentral.database.windows.net`
2. Click "Create database"
3. Name it: `School_YourSchoolName`
4. Choose Elastic Pool: SOSPool

---

## 7. Apply SchoolDb Migration to Your School Database

```bash
cd src/CleverSyncSOS.Core

dotnet ef database update --context SchoolDbContext --connection "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=School_YourSchoolName;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

---

## 8. Store School Connection String in Key Vault

```bash
az keyvault secret set \
  --vault-name cleversync-kv \
  --name CleverSyncSOS--YourSchool-Elementary--ConnectionString \
  --value "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=School_YourSchoolName;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

**Important**: The secret name uses the `SchoolPrefix` from the Schools table. Format: `CleverSyncSOS--{SchoolPrefix}--ConnectionString`

---

## 9. Store Clever API Credentials in Key Vault

```bash
# Store Clever Client ID
az keyvault secret set \
  --vault-name cleversync-kv \
  --name CleverSyncSOS--Clever--ClientId \
  --value "YOUR_CLEVER_CLIENT_ID"

# Store Clever Client Secret
az keyvault secret set \
  --vault-name cleversync-kv \
  --name CleverSyncSOS--Clever--ClientSecret \
  --value "YOUR_CLEVER_CLIENT_SECRET"
```

**Where to find these**:
1. Log in to [Clever Developer Dashboard](https://dev.clever.com/)
2. Select your application
3. Go to "Settings" → "OAuth"
4. Copy Client ID and Client Secret

---

## 10. Verify Setup

Run this checklist:

```bash
# 1. Verify Azure login
az account show

# 2. Check SessionDb has tables
# Connect to SessionDb and run: SELECT * FROM Districts;

# 3. Check school database has tables
# Connect to School_YourSchoolName and run: SELECT * FROM Students;

# 4. Verify Key Vault secrets exist
az keyvault secret list --vault-name cleversync-kv --query "[].name"
# Should show: CleverSyncSOS--Clever--ClientId, CleverSyncSOS--Clever--ClientSecret,
#              CleverSyncSOS--SessionDb--ConnectionString, Session-PW,
#              CleverSyncSOS--YourSchool-Elementary--ConnectionString
```

---

## 11. (Optional) Store Pre-Generated District Token

If you have a pre-generated district token from Clever (non-expiring token):

```bash
az keyvault secret set \
  --vault-name cleversync-kv \
  --name CleverSyncSOS--Clever--AccessToken \
  --value "YOUR_DISTRICT_TOKEN"
```

This is optional - the system can use OAuth to get tokens dynamically.

---

## 12. Configure Application Insights (Optional but Recommended)

For monitoring and telemetry:

```bash
# Create Application Insights instance
az monitor app-insights component create \
  --app CleverSyncSOS-Insights \
  --location eastus \
  --resource-group your-resource-group

# Get the connection string
az monitor app-insights component show \
  --app CleverSyncSOS-Insights \
  --resource-group your-resource-group \
  --query connectionString
```

Store the connection string for later use in API and Functions configuration.

---

## 13. Build and Test

```bash
# Build the entire solution
dotnet build

# Test authentication (Console app)
dotnet run --project src/CleverSyncSOS.Console

# Test sync
dotnet run --project src/CleverSyncSOS.Console sync

# Run Web API (for health checks)
dotnet run --project src/CleverSyncSOS.Api --urls "http://localhost:5000"

# Then visit: http://localhost:5000/health
```

---

## Summary of What You Created

After these steps, you should have:

✅ **SessionDb database** with:
- Districts table containing your district
- Schools table containing your school(s)
- SyncHistory table (empty, will populate during sync)

✅ **School database(s)** with:
- Students table (empty, will populate during sync)
- Teachers table (empty, will populate during sync)

✅ **Azure Key Vault** with:
- CleverSyncSOS--Clever--ClientId secret
- CleverSyncSOS--Clever--ClientSecret secret
- CleverSyncSOS--Clever--AccessToken secret (optional, for district tokens)
- CleverSyncSOS--SessionDb--ConnectionString secret
- Session-PW secret (SessionDb SQL password)
- CleverSyncSOS--{SchoolPrefix}--ConnectionString secret(s) (per-school connections)

✅ **Applications**:
- CleverSyncSOS.Console - Testing and manual operations
- CleverSyncSOS.Api - Health check endpoints (ready to deploy)
- CleverSyncSOS.Functions - Automated sync (ready to deploy)

✅ **Configuration**:
- appsettings.json with CleverApi and CleverAuth settings
- Application Insights connection string (optional but recommended)

---

## What You Need to Provide

To complete these steps, you need:

1. **SQL Server password** for user `SOSAdmin`
2. **Clever District ID** (from Clever dashboard)
3. **Clever School ID(s)** (from Clever dashboard)
4. **Clever OAuth credentials** (Client ID and Client Secret)
5. **Azure subscription** with permissions to create databases and Key Vault secrets
6. **Resource group name** (for Azure CLI commands)

---

## Next Steps - Deployment

After completing this setup, you're ready to deploy the production components:

### 1. Deploy Web API (Health Checks)

The Web API provides health check endpoints for monitoring.

```bash
# Publish the API
dotnet publish src/CleverSyncSOS.Api -c Release -o ./publish/api

# Deploy to Azure App Service (example)
az webapp up \
  --name CleverSyncSOS-API \
  --resource-group your-resource-group \
  --runtime "DOTNET:9.0"
```

Configure Application Settings in Azure Portal:
- `CleverAuth__KeyVaultUri` = `https://cleversync-kv.vault.azure.net/`
- `APPLICATIONINSIGHTS_CONNECTION_STRING` = (from step 12)

**Health Endpoints:**
- `https://your-api.azurewebsites.net/health` - Overall health
- `https://your-api.azurewebsites.net/health/clever-auth` - Clever auth status
- `https://your-api.azurewebsites.net/health/ready` - Readiness probe

### 2. Deploy Azure Functions (Automated Sync)

Azure Functions provide scheduled daily sync and manual on-demand sync.

```bash
# Create Function App
az functionapp create \
  --name CleverSyncSOS-Functions \
  --resource-group your-resource-group \
  --storage-account cleversyncsosfunc \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --runtime-version 9.0 \
  --functions-version 4

# Deploy
cd src/CleverSyncSOS.Functions
func azure functionapp publish CleverSyncSOS-Functions
```

Configure Application Settings:
- `CleverAuth__KeyVaultUri` = `https://cleversync-kv.vault.azure.net/`
- `CleverApi__BaseUrl` = `https://api.clever.com/v3.0`
- `CleverApi__PageSize` = `100`
- `APPLICATIONINSIGHTS_CONNECTION_STRING` = (from step 12)

**Functions:**
- **SyncTimer** - Runs daily at 2 AM UTC
- **ManualSync** - HTTP endpoint: `POST /api/sync`

### 3. Enable Managed Identity

For production, enable System-assigned Managed Identity:

```bash
# Enable on Function App
az functionapp identity assign \
  --name CleverSyncSOS-Functions \
  --resource-group your-resource-group

# Enable on Web API
az webapp identity assign \
  --name CleverSyncSOS-API \
  --resource-group your-resource-group

# Grant Key Vault access
FUNCTION_IDENTITY=$(az functionapp identity show \
  --name CleverSyncSOS-Functions \
  --resource-group your-resource-group \
  --query principalId -o tsv)

az keyvault set-policy \
  --name cleversync-kv \
  --object-id $FUNCTION_IDENTITY \
  --secret-permissions get list
```

### 4. Test Deployed System

```bash
# Test health endpoint
curl https://cleversyncsos-api.azurewebsites.net/health

# Test manual sync (requires function key)
curl -X POST \
  "https://cleversyncsos-functions.azurewebsites.net/api/sync?schoolId=3" \
  -H "x-functions-key: YOUR_FUNCTION_KEY"
```

### 5. Monitor in Production

- View logs in **Application Insights**
- Check **SyncHistory** table in SessionDb
- Monitor **health endpoints** for system status
- Set up **Azure Monitor alerts** for failures

---

## System Architecture (Current State)

```
┌─────────────────────────────────────────────────┐
│  CleverSyncSOS - Fully Implemented              │
├─────────────────────────────────────────────────┤
│  ✅ Stage 1: Authentication (100%)              │
│  ✅ Stage 2: Database Sync (100%)               │
│  ✅ Stage 3: Health & Observability (95%)       │
│  ✅ Stage 4: Azure Functions (100%)             │
└─────────────────────────────────────────────────┘
         │
         ├─► Azure Functions (Timer + HTTP)
         ├─► Web API (Health Checks)
         ├─► Console App (Testing)
         │
         ├─► Clever API (v3.0)
         ├─► Azure Key Vault (Credentials)
         ├─► Azure SQL (SessionDb + SchoolDbs)
         └─► Application Insights (Telemetry)
```

---

## Documentation

- **User Guide**: `docs/UserGuide.md` - For end users and administrators
- **Developer Guide**: `README.md` - For developers
- **Project Status**: `PROJECT-STATUS-FINAL.md` - Complete implementation details
- **Stage 4 Details**: `STAGE4-AZURE-FUNCTIONS-SUMMARY.md` - Azure Functions guide
- **Health Checks**: `HEALTH-CHECK-SUMMARY.md` - Health monitoring details

For questions or issues, see the User Guide or contact your IT support team

---

## Troubleshooting

### "Cannot connect to database"
- Check firewall rules: `az sql server firewall-rule list --server sos-northcentral --resource-group your-resource-group`
- Add your IP if needed: `az sql server firewall-rule create --server sos-northcentral --resource-group your-resource-group --name MyIP --start-ip-address YOUR_IP --end-ip-address YOUR_IP`

### "Key Vault access denied"
- Verify you're logged in: `az login`
- Check Key Vault policies: `az keyvault show --name cleversync-kv`

### "Clever API 401 Unauthorized"
- Verify secrets in Key Vault: `az keyvault secret show --vault-name cleversync-kv --name CleverSyncSOS--Clever--ClientId`
- Check credentials are correct in Clever dashboard

For detailed troubleshooting, see: [ConfigurationSetup.md](ConfigurationSetup.md)
