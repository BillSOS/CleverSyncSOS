# CleverSyncSOS Infrastructure

This directory contains Azure infrastructure as code (IaC) for deploying CleverSyncSOS using Bicep templates.

## Resources Deployed

The `main.bicep` template deploys the following Azure resources:

- **Azure Function App** - Serverless compute for sync operations (Linux Consumption Plan)
- **Azure SQL Server** - Managed SQL Server instance
- **SessionDb Database** - Control database for orchestration (Districts, Schools, SyncHistory)
- **Azure Key Vault** - Secure credential and connection string storage
- **Application Insights** - Telemetry and monitoring
- **Storage Account** - Required for Azure Functions runtime

## Prerequisites

1. **Azure CLI** - [Install Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
2. **Azure Subscription** - Active Azure subscription with Contributor access
3. **.NET 9 SDK** - For running database migrations
4. **Entity Framework Core Tools** - Install via `dotnet tool install --global dotnet-ef`

## Deployment

### Option 1: PowerShell (Windows/Linux/macOS)

```powershell
# Example deployment
$password = ConvertTo-SecureString "YourSecurePassword123!" -AsPlainText -Force

./deploy.ps1 `
    -ResourceGroupName "rg-cleversync-prod" `
    -Location "eastus" `
    -SqlAdminLogin "sqladmin" `
    -SqlAdminPassword $password `
    -Prefix "cleversync"
```

**Preview changes (WhatIf mode):**
```powershell
./deploy.ps1 `
    -ResourceGroupName "rg-cleversync-prod" `
    -SqlAdminLogin "sqladmin" `
    -SqlAdminPassword $password `
    -WhatIf
```

### Option 2: Bash (Linux/macOS)

```bash
# Make script executable
chmod +x deploy.sh

# Example deployment
./deploy.sh \
    --resource-group "rg-cleversync-prod" \
    --location "eastus" \
    --sql-admin-login "sqladmin" \
    --sql-admin-password "YourSecurePassword123!" \
    --prefix "cleversync"
```

**Preview changes (WhatIf mode):**
```bash
./deploy.sh \
    --resource-group "rg-cleversync-prod" \
    --sql-admin-login "sqladmin" \
    --sql-admin-password "YourSecurePassword123!" \
    --what-if
```

### Option 3: Azure CLI (Manual)

```bash
# Create resource group
az group create --name rg-cleversync-prod --location eastus

# Deploy Bicep template
az deployment group create \
    --resource-group rg-cleversync-prod \
    --template-file main.bicep \
    --parameters prefix=cleversync location=eastus sqlAdminLogin=sqladmin sqlAdminPassword='YourSecurePassword123!'
```

## Post-Deployment Steps

After infrastructure deployment completes, perform these steps:

### 1. Run Database Migrations

Navigate to the project root and run:

```bash
# Navigate to the data project
cd src/CleverSyncSOS.Core

# Set connection string (replace with your SQL Server FQDN)
$env:ConnectionStrings__SessionDb = "Server=tcp:cleversync-sql-xyz.database.windows.net,1433;Initial Catalog=SessionDb;User ID=sqladmin;Password=YourSecurePassword123!;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

# Apply SessionDb migrations
dotnet ef database update --context SessionDbContext

# Create a test school database (repeat for each school)
dotnet ef database update --context SchoolDbContext --connection "Server=tcp:cleversync-sql-xyz.database.windows.net,1433;Initial Catalog=School_Lincoln_Db;User ID=sqladmin;Password=YourSecurePassword123!;Encrypt=True;"
```

### 2. Add Clever API Credentials to Key Vault

```bash
# Get Key Vault name from deployment output
KV_NAME=$(az deployment group show --resource-group rg-cleversync-prod --name <deployment-name> --query properties.outputs.keyVaultUri.value -o tsv | cut -d'/' -f3 | cut -d'.' -f1)

# Add Clever credentials for each district
az keyvault secret set \
    --vault-name $KV_NAME \
    --name "CleverSyncSOS--District-TestDistrict--ClientId" \
    --value "<your-clever-client-id>"

az keyvault secret set \
    --vault-name $KV_NAME \
    --name "CleverSyncSOS--District-TestDistrict--ClientSecret" \
    --value "<your-clever-client-secret>"
```

### 3. Add School Connection Strings to Key Vault

```bash
# Add connection string for each school
az keyvault secret set \
    --vault-name $KV_NAME \
    --name "CleverSyncSOS--School-Lincoln--ConnectionString" \
    --value "Server=tcp:cleversync-sql-xyz.database.windows.net,1433;Initial Catalog=School_Lincoln_Db;User ID=sqladmin;Password=YourSecurePassword123!;Encrypt=True;"
```

### 4. Seed SessionDb with Districts and Schools

Create a migration or script to insert initial district/school records:

```sql
-- Example SQL to seed SessionDb
INSERT INTO Districts (DistrictId, CleverDistrictId, Name, KeyVaultSecretPrefix, IsActive)
VALUES ('TestDistrict', 'clever-district-id-123', 'Test District', 'CleverSyncSOS--District-TestDistrict', 1);

INSERT INTO Schools (SchoolId, DistrictId, CleverSchoolId, Name, DatabaseName, KeyVaultConnectionStringSecretName, IsActive, RequiresFullSync)
VALUES ('Lincoln', 'TestDistrict', 'clever-school-id-456', 'Lincoln Elementary', 'School_Lincoln_Db', 'CleverSyncSOS--School-Lincoln--ConnectionString', 1, 1);
```

### 5. Deploy Function App Code

```bash
# Navigate to Functions project
cd src/CleverSyncSOS.Functions

# Get Function App name from deployment output
FUNC_APP_NAME=$(az deployment group show --resource-group rg-cleversync-prod --name <deployment-name> --query properties.outputs.functionAppDefaultHostName.value -o tsv | cut -d'.' -f1)

# Publish to Azure
func azure functionapp publish $FUNC_APP_NAME
```

### 6. Test Health Check

```bash
# Test the health check endpoint
FUNC_APP_URL="https://$(az functionapp show --resource-group rg-cleversync-prod --name $FUNC_APP_NAME --query defaultHostName -o tsv)"

curl "$FUNC_APP_URL/api/health/clever-auth"
```

## Configuration

### SQL Server Configuration

- **SKU**: Basic (5 DTU) - Suitable for low-volume workloads. Upgrade to Standard/Premium for production.
- **Max Size**: 2 GB - Adjust `maxSizeBytes` in `main.bicep` for larger databases
- **Firewall**: Allows Azure services by default. Add client IP rules as needed:

```bash
az sql server firewall-rule create \
    --resource-group rg-cleversync-prod \
    --server <sql-server-name> \
    --name "MyClientIP" \
    --start-ip-address 1.2.3.4 \
    --end-ip-address 1.2.3.4
```

### Function App Configuration

All application settings are configured via the Bicep template. To update settings after deployment:

```bash
az functionapp config appsettings set \
    --resource-group rg-cleversync-prod \
    --name $FUNC_APP_NAME \
    --settings "CleverSync:Concurrency:MaxSchoolsInParallel=10"
```

## Security Considerations

1. **SQL Admin Password**: Never commit passwords to source control. Use:
   - Azure Key Vault references in parameters file (recommended)
   - Prompt at deployment time
   - Azure DevOps/GitHub Secrets for CI/CD

2. **Key Vault Access**: Function App uses system-assigned managed identity. No connection strings needed.

3. **Clever API Credentials**: Always store in Key Vault, never in app settings or code.

4. **TLS**: All resources enforce TLS 1.2+.

5. **Firewall Rules**: Review SQL Server firewall rules. Restrict to minimum required IPs in production.

## Monitoring

### Application Insights Queries

```kusto
// Recent sync operations
traces
| where timestamp > ago(1h)
| where customDimensions.Category == "CleverSyncSOS.Sync"
| project timestamp, message, customDimensions.SchoolId, customDimensions.EntityType, customDimensions.Status
| order by timestamp desc
```

### Alerts

Set up alerts in Azure Monitor for:
- Function failures (> 3 consecutive)
- SQL Database DTU utilization (> 80%)
- Key Vault access denied
- Sync duration (> 10 minutes)

## Troubleshooting

### Deployment Errors

**Error: Key Vault name already exists**
- Key Vault names are globally unique. Change `prefix` parameter or manually delete soft-deleted Key Vault:
  ```bash
  az keyvault purge --name <kv-name>
  ```

**Error: SQL Server name already exists**
- SQL Server names are globally unique. Change `prefix` parameter.

**Error: Insufficient permissions**
- Ensure you have `Contributor` role on the resource group.

### Post-Deployment Issues

**Function App won't start**
- Check Application Insights logs for startup errors
- Verify `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING` is set
- Ensure storage account is accessible

**Database connection failures**
- Verify connection string in Key Vault is correct
- Check SQL Server firewall rules allow Azure services
- Confirm Function App managed identity has Key Vault access

**Migrations fail**
- Ensure SQL admin credentials are correct
- Check if SQL Server firewall allows your client IP
- Verify database doesn't already exist (drop and recreate if needed)

## Cost Estimation

Approximate monthly costs (US East region, as of 2024):

| Resource | SKU | Monthly Cost |
|----------|-----|--------------|
| Function App | Consumption Plan (1M executions, 400k GB-s) | $20 |
| Azure SQL Database | Basic (5 DTU) | $5 |
| Application Insights | Pay-as-you-go (5 GB ingestion) | $12 |
| Storage Account | Standard LRS (10 GB) | $0.20 |
| Key Vault | Standard (10k operations) | $0.03 |
| **Total** | | **~$37/month** |

Upgrade SQL Database to Standard S2 (50 DTU) for production workloads: +$75/month.

## Clean Up

To delete all deployed resources:

```bash
# Delete resource group and all resources
az group delete --name rg-cleversync-prod --yes --no-wait

# Purge soft-deleted Key Vault (if needed)
az keyvault purge --name <kv-name>
```

## References

- [Azure Functions Bicep Reference](https://learn.microsoft.com/azure/templates/microsoft.web/sites)
- [Azure SQL Database Bicep Reference](https://learn.microsoft.com/azure/templates/microsoft.sql/servers/databases)
- [Azure Key Vault Bicep Reference](https://learn.microsoft.com/azure/templates/microsoft.keyvault/vaults)
- [CleverSyncSOS Specification](../SpecKit/spec.md)
- [Implementation Plan](../SpecKit/plan.md)
