# Configuration Setup Guide

## Overview

This guide walks you through the manual configuration steps needed to connect CleverSyncSOS to your Azure resources.

---

## 1. Store SessionDb Connection String in Azure Key Vault (REQUIRED)

**Security Best Practice**: Connection strings with passwords should NEVER be stored in appsettings.json or source control.

### A. Store SessionDb Connection String

Store your SessionDb connection string in Azure Key Vault:

```bash
az keyvault secret set \
  --vault-name cleversyncsos \
  --name SessionDb-ConnectionString \
  --value "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

**How it works**:
- The application automatically loads this connection string from Key Vault at startup (via `LoadSessionDbConnectionStringFromKeyVault()`)
- The password is never stored in configuration files
- Only users/applications with Key Vault access can retrieve the connection string

**Note**: The secret name `SessionDb-ConnectionString` matches the value in `appsettings.json`:
```json
"KeyVault": {
  "SessionDbConnectionStringSecretName": "SessionDb-ConnectionString"
}
```

---

## 2. Azure Key Vault Setup

### A. Verify Key Vault Exists

Your Key Vault: `https://cleversyncsos.vault.azure.net/`

Verify it exists:
```bash
az keyvault show --name cleversyncsos
```

### B. Store Clever API Credentials

Store your Clever OAuth credentials in Key Vault:

```bash
# Store Clever Client ID
az keyvault secret set --vault-name cleversyncsos --name CleverClientId --value "YOUR_CLEVER_CLIENT_ID"

# Store Clever Client Secret
az keyvault secret set --vault-name cleversyncsos --name CleverClientSecret --value "YOUR_CLEVER_CLIENT_SECRET"
```

### C. Store Per-School Connection Strings

For each school, store its database connection string in Key Vault:

```bash
# Example for Lincoln High School
az keyvault secret set --vault-name cleversyncsos --name School-LincolnHigh-ConnectionString --value "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=School_LincolnHigh;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"

# Example for Washington Middle School
az keyvault secret set --vault-name cleversyncsos --name School-WashingtonMiddle-ConnectionString --value "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=School_WashingtonMiddle;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

### D. Grant Access to Application

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
az keyvault set-policy --name cleversyncsos \
  --object-id <managed-identity-object-id> \
  --secret-permissions get list
```

#### Option 3: Service Principal (CI/CD)

```bash
# Create service principal
az ad sp create-for-rbac --name CleverSyncSOSSP

# Grant Key Vault access
az keyvault set-policy --name cleversyncsos \
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

INSERT INTO Districts (CleverDistrictId, Name, KeyVaultSecretPrefix, CreatedAt, UpdatedAt)
VALUES
('YOUR_CLEVER_DISTRICT_ID', 'Your District Name', 'District-YourDistrict', GETUTCDATE(), GETUTCDATE());
```

### B. Insert School Records

```sql
USE SessionDb;

-- Get the CleverDistrictId from the previous insert
DECLARE @DistrictId NVARCHAR(50) = (SELECT CleverDistrictId FROM Districts WHERE CleverDistrictId = 'YOUR_CLEVER_DISTRICT_ID');

-- Insert schools (DistrictId is now nvarchar(50) storing CleverDistrictId)
INSERT INTO Schools (DistrictId, CleverSchoolId, Name, DatabaseName, KeyVaultConnectionStringSecretName, IsActive, RequiresFullSync, CreatedAt, UpdatedAt)
VALUES
(@DistrictId, 'YOUR_CLEVER_SCHOOL_ID_1', 'Lincoln High School', 'School_LincolnHigh', 'School-LincolnHigh-ConnectionString', 1, 1, GETUTCDATE(), GETUTCDATE()),
(@DistrictId, 'YOUR_CLEVER_SCHOOL_ID_2', 'Washington Middle School', 'School_WashingtonMiddle', 'School-WashingtonMiddle-ConnectionString', 1, 1, GETUTCDATE(), GETUTCDATE());
```

**Note**: `RequiresFullSync = 1` ensures the first sync is a full sync.

---

## 5. Verify Configuration

### A. Test Key Vault Access

Create a simple test to verify Key Vault access:

```csharp
// In Program.cs or a test
var credential = new DefaultAzureCredential();
var secretClient = new SecretClient(new Uri("https://cleversyncsos.vault.azure.net/"), credential);

try
{
    var secret = await secretClient.GetSecretAsync("CleverClientId");
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
- [ ] **Azure Key Vault** contains:
  - [ ] `CleverClientId` secret
  - [ ] `CleverClientSecret` secret
  - [ ] `SessionDb-ConnectionString` secret (with SessionDb password)
  - [ ] `School-{SchoolName}-ConnectionString` secrets for each school
- [ ] **Key Vault access** configured (Azure CLI login, Managed Identity, or Service Principal)
- [ ] **Azure SQL Server** firewall rules allow your IP and Azure services
- [ ] **SessionDb database** created and migrations applied
- [ ] **Districts table** populated with your district data
- [ ] **Schools table** populated with your school data
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
- Verify Key Vault access policies: `az keyvault show --name cleversyncsos`
- Check managed identity has correct permissions

### Error: "The client does not have permission to perform action"

- Grant Key Vault permissions: `az keyvault set-policy --name cleversyncsos --object-id YOUR_OBJECT_ID --secret-permissions get list`

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
