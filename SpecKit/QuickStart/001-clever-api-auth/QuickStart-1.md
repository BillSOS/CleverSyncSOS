---
speckit:
  type: quickstart
  title: Clever API Authentication QuickStart
  owner: Bill Martin
  version: 1.0.0
  linked_spec: ../../Specs/001-clever-api-auth/spec-1.md
  linked_plan: ../../Plans/001-clever-api-auth/plan.md
---

# QuickStart: Clever API Authentication and Connection

## ✅ Prerequisites

- Azure subscription with access to Key Vault and App Service or Functions
- Clever API Client ID and Client Secret
- .NET 9 SDK installed
- Visual Studio 2022+ or VS Code
- GitHub repo cloned locally

---

## 🔐 Step 1: Provision Azure Key Vault [Phase: Core Implementation]

1. Create a Key Vault in your Azure subscription.
2. Add two secrets:
   - `CleverClientId`
   - `CleverClientSecret`
3. Assign a managed identity to your Azure App Service or Function.
4. Grant that identity `Secret Reader` access to the Key Vault.

---

## ⚙️ Step 2: Configure App Settings [Phase: Core Implementation]

In Azure App Configuration or `appsettings.json` (for local dev):

```json
{
  "CleverAuth": {
    "TokenEndpoint": "https://clever.com/oauth/token",
    "Scope": "read:students read:teachers",
    "RetryPolicy": {
      "MaxRetries": 5,
      "BaseDelaySeconds": 2
    },
    "HealthCheckIntervalSeconds": 30
  }
}
```

---

## 🧩 Step 3: Register Services in DI Container [Phase: Core Implementation]

In `Program.cs` or `Startup.cs`:

```csharp
services.AddHttpClient<ICleverAuthenticationService, CleverAuthenticationService>()
        .AddPolicyHandler(CleverApiRetryPolicy.GetPolicy());

services.AddSingleton<ICredentialStore, KeyVaultCredentialStore>();
services.AddHealthChecks()
        .AddCheck<CleverAuthenticationHealthCheck>("clever-auth");
```

---

## 🧪 Step 4: Verify Authentication [Phase: Health & Observability]

1. Run the app locally or deploy to Azure.
2. Hit the health check endpoint:

   ```http
   GET /health/clever-auth
   ```

3. Confirm response includes:
   - `status: Healthy`
   - `lastSuccessTimestamp`
   - `error: null`

---

## 🔁 Step 5: Test Token Refresh [Phase: Testing]

1. Simulate token expiration by reducing lifetime in mock config.
2. Confirm proactive refresh occurs before expiration.
3. Validate retry logic by temporarily disabling Key Vault access.

---

## 📊 Step 6: Monitor Logs and Telemetry [Phase: Health & Observability]

- Use Azure Application Insights to query:
  - `CleverAuthTokenAcquired`
  - `CleverAuthTokenRefreshed`
  - `CleverAuthFailure`
- Confirm no secrets appear in logs.

---

## 🚀 Step 7: CI/CD Integration [Phase: Deployment & Validation]

1. Add build and test steps to GitHub Actions or Azure DevOps.
2. Deploy to Azure Functions or App Service.
3. Validate health check and telemetry post-deployment.

---
