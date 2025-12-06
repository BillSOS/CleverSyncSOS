# Configuration Setup Guide

**Version:** 3.0.0
**Last Updated:** 2025-11-27

## Overview

This guide walks you through the manual configuration steps needed to connect CleverSyncSOS to your Azure resources.

**Prefer a GUI?** For a graphical interface to manage configuration, see the [Admin Portal Quick Start Guide](AdminPortal-QuickStart.md).

---

## Key Vault Naming Convention

CleverSyncSOS v2.0 uses a simplified secret naming convention:
- **Global secrets**: `{FunctionalName}` (e.g., `ClientId`, `SuperAdminPassword`)
- **District secrets**: `{DistrictPrefix}--{FunctionalName}` (e.g., `NorthCentral--ApiToken`)
- **School secrets**: `{SchoolPrefix}--{FunctionalName}` (e.g., `CityHighSchool--ConnectionString`)

For complete details, see [Key Vault Naming Convention](./KeyVaultNamingConvention.md).

---

## 1. Store SessionDb Connection String in Azure Key Vault (REQUIRED)

**Security Best Practice**: Connection strings with passwords should NEVER be stored in appsettings.json or source control.

### A. Store SessionDb Connection String (Optional)

Store your SessionDb connection string in Azure Key Vault:

```bash
az keyvault secret set \
  --vault-name cleversync-kv \
  --name SessionDbConnectionString \
  --value "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

### B. Store SessionDb Password (Recommended)

The Admin Portal requires the SessionDb password separately for constructing connection strings:

```bash
az keyvault secret set \
  --vault-name cleversync-kv \
  --name SessionDbPassword \
  --value "YOUR_PASSWORD"
```

**How it works**:
- The application loads the password from Key Vault at startup
- The password is injected into the connection string template from appsettings.json
- Passwords are never stored in configuration files
- Only users/applications with Key Vault access can retrieve secrets

**Note**: The secret name changed from `Session-PW` (v1.0) to `SessionDbPassword` (v2.0) for consistency.

---

## 2. Azure Key Vault Setup

### A. Verify Key Vault Exists

**Production Key Vault:** `https://cleversync-kv.vault.azure.net/`

Verify it exists:
```bash
az keyvault show --name cleversync-kv
```

**Note:** This is the standardized Key Vault name for the CleverSyncSOS production environment.

### B. Store Clever API Credentials

Store your Clever OAuth credentials in Key Vault using the standardized naming pattern:

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

### C. Store Admin Portal Super Admin Password

For emergency Admin Portal access:

```bash
# Generate a secure password (or use your own)
az keyvault secret set \
  --vault-name cleversync-kv \
  --name CleverSyncSOS--AdminPortal--SuperAdminPassword \
  --value "$(openssl rand -base64 32)"
```

**Note**: Save this password securely - it provides emergency access to the Admin Portal.

### D. Store Per-School Connection Strings

For each school, store its database connection string in Key Vault using the school's `SchoolPrefix`:

```bash
# Example for Lincoln Elementary School (SchoolPrefix: Lincoln-Elementary)
az keyvault secret set \
  --vault-name cleversync-kv \
  --name CleverSyncSOS--Lincoln-Elementary--ConnectionString \
  --value "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=School_LincolnHigh;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"

# Example for Washington Middle School (SchoolPrefix: Washington-Middle)
az keyvault secret set \
  --vault-name cleversync-kv \
  --name CleverSyncSOS--Washington-Middle--ConnectionString \
  --value "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=School_WashingtonMiddle;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

**Important**: The secret name must use the `SchoolPrefix` value from the Schools table. Format: `CleverSyncSOS--{SchoolPrefix}--ConnectionString`

### E. Grant Access to Application

The application uses **DefaultAzureCredential** to authenticate to Key Vault. Configure access:

#### Option 1: Local Development (Azure CLI)

```bash
# Sign in to Azure
az login

# Your local Azure CLI identity will be used
```

#### Option 2: Managed Identity (Production - Azure Functions/App Service)

```bash
# Enable system-assigned managed identity (when deploying to Azure)
az functionapp identity assign --name your-function-app --resource-group your-resource-group

# Grant Key Vault access to the managed identity
az keyvault set-policy --name cleversync-kv \
  --object-id <managed-identity-object-id> \
  --secret-permissions get list
```

#### Option 3: Service Principal (CI/CD)

```bash
# Create service principal
az ad sp create-for-rbac --name CleverSyncSOSSP

# Grant Key Vault access
az keyvault set-policy --name cleversync-kv \
  --spn <service-principal-app-id> \
  --secret-permissions get list
```

---

## 3. Azure SQL Database Setup

### A. Create SessionDb Database

If not already created:

```bash
# Create database
az sql db create \
  --resource-group your-resource-group \
  --server sos-northcentral \
  --name SessionDb \
  --service-objective S0 \
  --backup-storage-redundancy Local
```

### B. Configure Firewall Rules

Allow your IP address to connect:

```bash
# Add your current IP
az sql server firewall-rule create \
  --resource-group your-resource-group \
  --server sos-northcentral \
  --name AllowMyIP \
  --start-ip-address YOUR_IP \
  --end-ip-address YOUR_IP

# For Azure services (Functions, App Service, etc.)
az sql server firewall-rule create \
  --resource-group your-resource-group \
  --server sos-northcentral \
  --name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

### C. Apply SessionDb Migrations

Navigate to the Core project and apply migrations:

```bash
cd src/CleverSyncSOS.Core

# Apply SessionDb migrations
dotnet ef database update --context SessionDbContext --connection "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

### D. Create Per-School Databases

For each school, create a dedicated database:

```bash
# Example for Lincoln High School
az sql db create \
  --resource-group your-resource-group \
  --server sos-northcentral \
  --name School_LincolnHigh \
  --service-objective S0

# Apply SchoolDb migrations
cd src/CleverSyncSOS.Core
dotnet ef database update --context SchoolDbContext --connection "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=School_LincolnHigh;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

---

## 4. Populate SessionDb with Districts and Schools

After applying SessionDb migrations, populate the database with your district and school data:

### A. Insert District Record

```sql
USE SessionDb;

INSERT INTO Districts (CleverDistrictId, Name, DistrictPrefix, CreatedAt, UpdatedAt)
VALUES
('YOUR_CLEVER_DISTRICT_ID', 'Your District Name', 'YourDistrict-District', GETUTCDATE(), GETUTCDATE());
```

**Note**: `DistrictPrefix` is used for Key Vault secret naming. Format: `{DistrictName}-District`

### B. Insert School Records

```sql
USE SessionDb;

-- Get the CleverDistrictId from the previous insert
DECLARE @DistrictId NVARCHAR(50) = (SELECT CleverDistrictId FROM Districts WHERE CleverDistrictId = 'YOUR_CLEVER_DISTRICT_ID');

-- Insert schools (DistrictId is now nvarchar(50) storing CleverDistrictId)
INSERT INTO Schools (DistrictId, CleverSchoolId, Name, DatabaseName, SchoolPrefix, IsActive, RequiresFullSync, CreatedAt, UpdatedAt)
VALUES
(@DistrictId, 'YOUR_CLEVER_SCHOOL_ID_1', 'Lincoln High School', 'School_LincolnHigh', 'Lincoln-High', 1, 1, GETUTCDATE(), GETUTCDATE()),
(@DistrictId, 'YOUR_CLEVER_SCHOOL_ID_2', 'Washington Middle School', 'School_WashingtonMiddle', 'Washington-Middle', 1, 1, GETUTCDATE(), GETUTCDATE());
```

**Note**:
- `SchoolPrefix` is used for Key Vault secret naming. Format: `{SchoolName}-{Level}`
- `RequiresFullSync = 1` ensures the first sync is a full sync

---

## 5. Verify Configuration

### A. Test Key Vault Access

Create a simple test to verify Key Vault access:

```csharp
// In Program.cs or a test
var credential = new DefaultAzureCredential();
var secretClient = new SecretClient(new Uri("https://cleversync-kv.vault.azure.net/"), credential);

try
{
    var secret = await secretClient.GetSecretAsync("CleverSyncSOS--Clever--ClientId");
    Console.WriteLine($"Successfully retrieved secret: {secret.Value.Name}");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to access Key Vault: {ex.Message}");
}
```

### B. Test Database Connection

Verify SessionDb connection:

```bash
# Using sqlcmd (if installed)
sqlcmd -S sos-northcentral.database.windows.net -d SessionDb -U SOSAdmin -P YOUR_PASSWORD -Q "SELECT COUNT(*) FROM Districts"

# Or use Azure Data Studio / SQL Server Management Studio
```

### C. Run Build

Ensure everything compiles:

```bash
dotnet build
```

---

## 6. Configuration Summary Checklist

Before running the application, verify:

- [ ] **appsettings.json** contains CleverApi settings (NO passwords)
- [ ] **Azure Key Vault** (`cleversync-kv`) contains:
  - [ ] `CleverSyncSOS--Clever--ClientId` secret
  - [ ] `CleverSyncSOS--Clever--ClientSecret` secret
  - [ ] `CleverSyncSOS--SessionDb--ConnectionString` secret
  - [ ] `Session-PW` secret (SessionDb password for Admin Portal)
  - [ ] `CleverSyncSOS--AdminPortal--SuperAdminPassword` secret
  - [ ] `CleverSyncSOS--{SchoolPrefix}--ConnectionString` secrets for each school
- [ ] **Key Vault access** configured (Azure CLI login, Managed Identity, or Service Principal)
- [ ] **Azure SQL Server** firewall rules allow your IP and Azure services
- [ ] **SessionDb database** created and migrations applied
- [ ] **Districts table** populated with `DistrictPrefix` values
- [ ] **Schools table** populated with `SchoolPrefix` values
- [ ] **Per-school databases** created and SchoolDb migrations applied
- [ ] **Connection strings** stored in Key Vault match actual school databases

---

## 7. Security Architecture

The application uses a multi-layered security approach:

### Primary Method: Azure Key Vault (Default)

All connection strings and credentials are stored in Azure Key Vault and loaded at startup:

```
Startup → LoadSessionDbConnectionStringFromKeyVault()
       → Retrieves secret from Key Vault
       → Injects into Configuration["ConnectionStrings:SessionDb"]
       → AddCleverSync() uses connection string to register DbContext
```

### Alternative Method: Environment Variables (Local Development Only)

For local development without Key Vault access, you can use environment variables:

#### Windows (PowerShell)
```powershell
$env:ConnectionStrings__SessionDb="Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

#### Linux/Mac (Bash)
```bash
export ConnectionStrings__SessionDb="Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

**Note**: This bypasses Key Vault. The application will use the environment variable if Key Vault loading fails.

### Future Enhancement: Azure Managed Identity

For production deployments, consider using Azure Managed Identity to connect to SQL Database without passwords:

```csharp
// Connection string with Managed Identity (no password)
"Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;Authentication=Active Directory Default;Encrypt=True;"
```

This requires:
- Enabling Managed Identity on your Azure Function/App Service
- Granting the Managed Identity access to SQL Database
- Using `Authentication=Active Directory Default` in connection string

---

## 8. Troubleshooting

### Error: "Cannot open database 'SessionDb'"

- Verify the database exists: `az sql db show --name SessionDb --server sos-northcentral --resource-group your-resource-group`
- Check firewall rules allow your IP
- Verify connection string is correct

### Error: "401 Unauthorized" from Key Vault

- Ensure you're logged in: `az login`
- Verify Key Vault access policies: `az keyvault show --name cleversync-kv`
- Check managed identity has correct permissions

### Error: "The client does not have permission to perform action"

- Grant Key Vault permissions: `az keyvault set-policy --name cleversync-kv --object-id YOUR_OBJECT_ID --secret-permissions get list`

### Error: "Secret not found"

- Verify secret exists: `az keyvault secret list --vault-name cleversync-kv`
- Check secret name uses correct format: `CleverSyncSOS--{Component}--{Property}`
- Ensure SchoolPrefix or DistrictPrefix matches database values

### Error: "A connection was successfully established but an error occurred during login"

- Password is incorrect or special characters need escaping
- User ID is incorrect
- SQL Server authentication might be disabled

---

## Next Steps

After completing this configuration:

1. **Test the connection** by running the console application
2. **Run a manual sync** using `ISyncService.SyncSchoolAsync()`
3. **Set up Azure Functions** for scheduled/manual sync triggers
4. **Configure Application Insights** for observability

## Related Documentation

- [Database Migrations Guide](DatabaseMigrations.md)
- [Sync Service Implementation](../src/CleverSyncSOS.Core/Sync/SyncService.cs)
- [Azure Key Vault Documentation](https://docs.microsoft.com/en-us/azure/key-vault/)
