# Security Architecture

## Overview

CleverSyncSOS implements security best practices for credential and connection string management using Azure Key Vault.

---

## Key Security Principles

### 1. **No Secrets in Source Control**

- ❌ Connection strings with passwords are NEVER stored in `appsettings.json`
- ❌ API credentials are NEVER hardcoded in configuration files
- ✅ All secrets are stored in Azure Key Vault
- ✅ Configuration files only contain Key Vault references and non-sensitive settings

### 2. **Centralized Secret Management**

All sensitive data is stored in Azure Key Vault following standardized naming convention:

**Pattern**: `CleverSyncSOS--{Component}--{Property}`

| Secret Name | Purpose | Example Value |
|------------|---------|---------------|
| `CleverSyncSOS--Clever--ClientId` | Clever OAuth Client ID | `abc123def456` |
| `CleverSyncSOS--Clever--ClientSecret` | Clever OAuth Client Secret | `secret_abc123` |
| `CleverSyncSOS--SessionDb--ConnectionString` | SessionDb database connection | `Server=...;Password=...` |
| `CleverSyncSOS--AdminPortal--SuperAdminPassword` | Super Admin bypass login password | `generated_secure_password` |
| `CleverSyncSOS--{SchoolPrefix}--ConnectionString` | Per-school database connections | `Server=...;Password=...` |
| `Session-PW` | SessionDb SQL password | `sql_password` |

**Note**: School-specific secrets use the school's `SchoolPrefix` (e.g., `CleverSyncSOS--Lincoln-Elementary--ConnectionString`)

### 3. **DefaultAzureCredential for Authentication**

The application uses `DefaultAzureCredential` which automatically tries multiple authentication methods:

1. **Environment variables** (Azure service principal)
2. **Managed Identity** (when running in Azure)
3. **Azure CLI** (for local development)
4. **Visual Studio** (for local development)
5. **Visual Studio Code** (for local development)

This means:
- ✅ No authentication credentials needed in configuration
- ✅ Works locally with `az login`
- ✅ Works in Azure with Managed Identity
- ✅ Secure by default

---

## How It Works

### Startup Flow

```
1. Application starts
   ↓
2. LoadSessionDbConnectionStringFromKeyVault() called
   ↓
3. DefaultAzureCredential authenticates to Key Vault
   ↓
4. Retrieves password from "Session-PW" secret
   ↓
5. Constructs connection string with password
   ↓
6. AddCleverSync() registers SessionDbContext with connection string
   ↓
7. Application is ready (SessionDb password never stored in config)
```

### Runtime Flow for School Databases

```
1. SyncService.SyncSchoolAsync() called
   ↓
2. SchoolDatabaseConnectionFactory.CreateSchoolContextAsync(school)
   ↓
3. ICredentialStore.GetSecretAsync(school.KeyVaultConnectionStringSecretName)
   ↓
4. DefaultAzureCredential authenticates to Key Vault
   ↓
5. Retrieves school-specific connection string
   ↓
6. Creates SchoolDbContext with connection string
   ↓
7. Returns DbContext (school password never stored in config)
```

---

## Code Implementation

### Key Vault Configuration Loader

**File**: `src/CleverSyncSOS.Infrastructure/Extensions/KeyVaultConfigurationExtensions.cs`

```csharp
public static void LoadSessionDbConnectionStringFromKeyVault(this IConfiguration configuration)
{
    var keyVaultUri = configuration["CleverAuth:KeyVaultUri"];
    var secretName = configuration["KeyVault:SessionDbConnectionStringSecretName"];

    var credential = new DefaultAzureCredential();
    var secretClient = new SecretClient(new Uri(keyVaultUri), credential);

    var secret = await secretClient.GetSecretAsync(secretName);

    // Inject into configuration (in-memory only)
    configuration["ConnectionStrings:SessionDb"] = secret.Value.Value;
}
```

### Credential Store

**File**: `src/CleverSyncSOS.Core/Authentication/AzureKeyVaultCredentialStore.cs`

```csharp
public async Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
{
    var secret = await _secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken);
    return secret.Value.Value;
}
```

### Service Registration

**File**: `src/CleverSyncSOS.Console/Program.cs`

```csharp
.ConfigureServices((context, services) =>
{
    // Load SessionDb connection string from Key Vault (not from appsettings.json)
    context.Configuration.LoadSessionDbConnectionStringFromKeyVault();

    // Register services
    services.AddCleverAuthentication(context.Configuration);
    services.AddCleverApiClient(context.Configuration);
    services.AddCleverSync(context.Configuration);  // Uses loaded connection string
})
```

---

## Configuration Files

### appsettings.json (Secure)

```json
{
  "ConnectionStrings": {
    "SessionDb": ""  // Empty - loaded from Key Vault at startup
  },
  "AzureKeyVault": {
    "VaultUri": "https://cleversync-kv.vault.azure.net/"
  },
  "CleverAuth": {
    "KeyVaultUri": "https://cleversync-kv.vault.azure.net/",
    "ClientIdSecretName": "CleverSyncSOS--Clever--ClientId",
    "ClientSecretSecretName": "CleverSyncSOS--Clever--ClientSecret",
    "TokenEndpoint": "https://clever.com/oauth/tokens"
  },
  "CleverApi": {
    "BaseUrl": "https://api.clever.com/v3.0",
    "PageSize": 100,
    "MaxRetries": 5
  }
}
```

**Notice**:
- ✅ No passwords
- ✅ No connection strings with credentials
- ✅ Only Key Vault URI and secret names
- ✅ Safe to commit to source control

---

## Access Control

### Development Environment

```bash
# Authenticate to Azure
az login

# Verify Key Vault access
az keyvault secret list --vault-name cleversyncsos
```

Your Azure account needs:
- **Key Vault Secret User** role (or higher) on the Key Vault

### Production Environment (Azure Functions/App Service)

1. **Enable Managed Identity**:
   ```bash
   az functionapp identity assign --name your-function-app --resource-group your-resource-group
   ```

2. **Grant Key Vault Access**:
   ```bash
   az keyvault set-policy --name cleversyncsos \
     --object-id <managed-identity-object-id> \
     --secret-permissions get list
   ```

3. **Deploy Application** - No configuration changes needed, Managed Identity is automatically used

---

## Security Benefits

### 1. Secrets Rotation

Change passwords without redeploying:

```bash
# Update SessionDb password
az keyvault secret set --vault-name cleversync-kv \
  --name Session-PW \
  --value "NEW_PASSWORD"

# Update Clever API client secret
az keyvault secret set --vault-name cleversync-kv \
  --name CleverSyncSOS--Clever--ClientSecret \
  --value "NEW_CLIENT_SECRET"

# Update school connection string
az keyvault secret set --vault-name cleversync-kv \
  --name CleverSyncSOS--Lincoln-Elementary--ConnectionString \
  --value "Server=...;Password=NEW_PASSWORD;..."

# Application picks up new secrets on next startup (or restart app)
```

### 2. Audit Trail

Key Vault logs all secret access:

```bash
# View audit logs
az monitor activity-log list --resource-id /subscriptions/{sub-id}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/cleversyncsos
```

### 3. Access Control

Fine-grained permissions:
- Developers: Read-only access to dev Key Vault
- CI/CD: Read-only access to prod Key Vault
- Production apps: Managed Identity with minimal permissions

### 4. Separation of Duties

- **Developers**: Write code, cannot access production secrets
- **Operations**: Manage Key Vault, grant access as needed
- **Applications**: Use Managed Identity, no credentials in code

---

## Best Practices Implemented

✅ **Secrets in Key Vault** - All passwords and credentials stored securely

✅ **DefaultAzureCredential** - No hardcoded authentication

✅ **Managed Identity** - Production apps use identity-based authentication

✅ **No secrets in source control** - Configuration files are safe to commit

✅ **Least privilege** - Applications request only secrets they need

✅ **Secrets rotation** - Can update passwords without code changes

✅ **Audit logging** - Track all secret access

✅ **Environment separation** - Dev/staging/prod use different Key Vaults

---

## Comparison: Before vs After

### ❌ Insecure Approach (NOT USED)

```json
// appsettings.json - NEVER DO THIS
{
  "ConnectionStrings": {
    "SessionDb": "Server=...;Password=MyPassword123;..."
  }
}
```

**Problems**:
- Password visible in source control
- Password in deployment artifacts
- Must redeploy to change password
- No audit trail
- Anyone with repo access has password

### ✅ Secure Approach (IMPLEMENTED)

```json
// appsettings.json - SAFE
{
  "ConnectionStrings": {
    "SessionDb": ""  // Loaded from Key Vault
  },
  "KeyVault": {
    "SessionDbConnectionStringSecretName": "SessionDb-ConnectionString"
  }
}
```

**Benefits**:
- ✅ No password in source control
- ✅ Password stored in Key Vault
- ✅ Can rotate without redeploy
- ✅ Full audit trail
- ✅ Access controlled by Azure RBAC

---

## Future Enhancements

### Managed Identity for SQL Database (Passwordless)

Instead of username/password, use Managed Identity:

```csharp
// Connection string with Managed Identity (no password!)
"Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;Authentication=Active Directory Default;Encrypt=True;"
```

**Benefits**:
- No password to manage
- No password to rotate
- More secure
- Simplified management

**Requirements**:
- Enable Managed Identity on Azure Function/App Service
- Grant SQL permissions to Managed Identity
- Update connection string to use `Authentication=Active Directory Default`

---

## Admin Portal Security

The CleverSync Admin Portal implements additional security measures:

### 1. **Authentication Methods**

- **Clever OAuth**: School and district administrators authenticate via Clever SSO
- **Super Admin Bypass**: Emergency access using password from Key Vault (`CleverSyncSOS--AdminPortal--SuperAdminPassword`)
- **Rate Limiting**: Bypass login is rate-limited to prevent brute force attacks (5 attempts per hour per IP)

### 2. **Role-Based Access Control**

| Role | Access Level | Permissions |
|------|-------------|-------------|
| **SuperAdmin** | All districts and schools | Full access to all features, users, audit logs, configuration |
| **DistrictAdmin** | Assigned district only | Manage schools in district, view logs, configure district settings |
| **SchoolAdmin** | Assigned school only | View school data, sync operations, basic configuration |

### 3. **Session Management**

- Session timeout: 30 minutes of inactivity
- Sliding expiration: Activity extends session
- Secure cookies: HttpOnly, SameSite=Strict, Secure flag enforced
- HTTPS only: All traffic encrypted in transit

### 4. **Key Vault Access**

The Admin Portal uses Managed Identity to access Key Vault:

```bash
# Grant Admin Portal access to Key Vault
az webapp identity assign --name cleversyncprod-admin --resource-group cleversyncprod-rg

PRINCIPAL_ID=$(az webapp identity show \
  --name cleversyncprod-admin \
  --resource-group cleversyncprod-rg \
  --query principalId -o tsv)

az keyvault set-policy --name cleversync-kv \
  --object-id $PRINCIPAL_ID \
  --secret-permissions get list
```

### 5. **Audit Logging**

All administrative actions are logged to the `AuditLogs` table:
- User authentication (success/failure)
- Secret access (view/edit/delete)
- Configuration changes
- User management actions

### 6. **Sensitive Data Protection**

- Passwords displayed as `••••••••` by default
- Explicit action required to view secrets
- No secrets in browser console or network logs
- Secrets never cached in browser

---

## Related Documentation

- [Admin Portal Quick Start Guide](AdminPortal-QuickStart.md)
- [Admin Portal User Guide](AdminPortal-User-Guide.md)
- [Configuration Setup Guide](ConfigurationSetup.md)
- [Naming Conventions](Naming-Conventions.md)
- [Deployment Checklist](DEPLOYMENT-CHECKLIST.md)
- [Azure Key Vault Best Practices](https://docs.microsoft.com/en-us/azure/key-vault/general/best-practices)
