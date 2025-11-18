# CI/CD Setup Guide for CleverSyncSOS

This guide explains how to configure and use the continuous delivery pipeline for CleverSyncSOS.

## Overview

The CI/CD pipeline automatically:
1. **Builds** the .NET solution
2. **Tests** all unit and integration tests
3. **Deploys** Azure infrastructure (on push to master)
4. **Deploys** the Azure Functions application (on push to master)
5. **Validates** the deployment with smoke tests

## Pipeline Workflow

### On Pull Requests
- Builds the solution
- Runs all tests
- No deployment occurs

### On Push to Master
- Builds the solution
- Runs all tests
- Deploys Azure infrastructure (idempotent)
- Deploys the Functions app
- Runs smoke tests

## Prerequisites

### 1. Azure Service Principal

You need to create an Azure Service Principal with appropriate permissions:

```bash
# Login to Azure
az login

# Set your subscription
az account set --subscription "YOUR_SUBSCRIPTION_ID"

# Create a service principal with Contributor role
az ad sp create-for-rbac \
  --name "CleverSyncSOS-GitHub-Actions" \
  --role Contributor \
  --scopes /subscriptions/YOUR_SUBSCRIPTION_ID/resourceGroups/CleverSyncSOS-rg \
  --sdk-auth
```

This command will output JSON credentials. Save this output - you'll need it for GitHub secrets.

### 2. GitHub Secrets Configuration

Add the following secrets to your GitHub repository:

**Path:** Settings → Secrets and variables → Actions → New repository secret

#### Required Secrets

| Secret Name | Description | How to Get It |
|------------|-------------|---------------|
| `AZURE_CREDENTIALS` | Azure Service Principal credentials | Output from the `az ad sp create-for-rbac` command above (entire JSON) |

**Example of AZURE_CREDENTIALS format:**
```json
{
  "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "clientSecret": "your-client-secret",
  "subscriptionId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

## Setting Up GitHub Secrets

### Step-by-Step Instructions

1. Go to your GitHub repository: `https://github.com/BillSOS/CleverSyncSOS`

2. Click on **Settings** (top navigation bar)

3. In the left sidebar, expand **Secrets and variables** → Click **Actions**

4. Click **New repository secret**

5. Add the `AZURE_CREDENTIALS` secret:
   - **Name:** `AZURE_CREDENTIALS`
   - **Value:** Paste the entire JSON output from the service principal creation
   - Click **Add secret**

## Environment Configuration

The pipeline uses these environment variables (configured in `.github/workflows/cd-pipeline.yml`):

```yaml
DOTNET_VERSION: '9.0.x'                    # .NET SDK version
AZURE_RESOURCE_GROUP: 'CleverSyncSOS-rg'  # Azure resource group name
AZURE_LOCATION: 'eastus'                   # Azure region
FUNCTION_APP_NAME: 'cleversync-fn'        # Function App name
BICEP_FILE: 'infra/main.bicep'            # Path to Bicep template
```

**To customize these values:** Edit `.github/workflows/cd-pipeline.yml` and update the `env:` section.

## Manual Deployment Trigger

You can manually trigger the pipeline:

1. Go to **Actions** tab in GitHub
2. Select **CI/CD Pipeline** workflow
3. Click **Run workflow**
4. Select the branch (master)
5. Click **Run workflow** button

## Pipeline Jobs Explained

### 1. Build and Test Job
- Restores NuGet packages
- Compiles the solution in Release mode
- Runs unit tests (`CleverSyncSOS.Core.Tests`)
- Runs integration tests (`CleverSyncSOS.Integration.Tests`)
- Publishes the Functions app as an artifact
- Reports test results

### 2. Deploy Infrastructure Job
- Only runs on push to master
- Logs into Azure using service principal
- Creates/updates the resource group
- Deploys the Bicep template (idempotent)
- Verifies the Function App was created

**Resources Deployed:**
- Azure Functions (Consumption Plan, Linux)
- Application Insights
- Storage Account
- Key Vault (with managed identity access)

### 3. Deploy Application Job
- Only runs after infrastructure deployment succeeds
- Downloads the Functions artifact
- Deploys to Azure Functions using Web Deploy
- Verifies the deployment health

### 4. Smoke Tests Job
- Checks Function App is in "Running" state
- Lists all deployed functions
- Validates basic connectivity

## Monitoring Deployments

### GitHub Actions
- View deployment status: `https://github.com/BillSOS/CleverSyncSOS/actions`
- Click on any workflow run to see detailed logs

### Azure Portal
- Function App: `https://portal.azure.com/#resource/subscriptions/{sub-id}/resourceGroups/CleverSyncSOS-rg/providers/Microsoft.Web/sites/cleversync-fn`
- Application Insights: Monitor function executions and errors
- Function App → Deployment Center: View deployment history

## Troubleshooting

### Common Issues

#### 1. `AZURE_CREDENTIALS` Secret Not Found
**Error:** `Error: Could not find secret 'AZURE_CREDENTIALS'`

**Solution:** Ensure the secret is added to GitHub (see "Setting Up GitHub Secrets" above)

#### 2. Azure Permission Denied
**Error:** `Authorization failed` or `Forbidden`

**Solution:**
- Verify service principal has Contributor role on resource group
- Re-create the service principal with correct permissions

#### 3. Resource Group Not Found
**Error:** `ResourceGroupNotFound`

**Solution:**
- The pipeline creates the resource group automatically
- Ensure `AZURE_RESOURCE_GROUP` name matches your Azure subscription naming policies

#### 4. Function App Deployment Fails
**Error:** `Deployment failed with status code 409` or `Conflict`

**Solution:**
- Function App might be locked or in a transitional state
- Wait a few minutes and re-run the workflow
- Check Azure Portal for Function App status

#### 5. Tests Failing
**Error:** Test job fails

**Solution:**
- Review test logs in GitHub Actions
- Run tests locally: `dotnet test CleverSyncSOS.sln`
- Fix failing tests before merging to master

## Best Practices

### 1. Branch Protection
Enable branch protection on `master`:
- Require pull request reviews
- Require status checks to pass before merging
- Include the "Build and Test" job as a required check

### 2. Environment Separation
For production workloads, consider:
- Creating separate environments (dev, staging, production)
- Using GitHub Environments with approvals
- Separate resource groups per environment

Example environment setup:
```yaml
deploy-production:
  environment:
    name: production
    url: https://cleversync-fn.azurewebsites.net
  # ... rest of deployment steps
```

### 3. Secrets Management
- Never commit secrets to the repository
- Rotate service principal credentials regularly
- Use Azure Key Vault for application secrets
- Store only deployment credentials in GitHub Secrets

### 4. Monitoring
- Set up Application Insights alerts for errors
- Monitor Function App metrics (executions, failures, duration)
- Review deployment logs regularly

## Deployment Frequency

- **Automatic:** Every push to `master` triggers a deployment
- **Manual:** Use workflow_dispatch in GitHub Actions UI
- **Rollback:** Revert the git commit and push to redeploy previous version

## Infrastructure as Code

The pipeline deploys infrastructure using `infra/main.bicep`. Changes to infrastructure:

1. **Modify** `infra/main.bicep`
2. **Test locally** (optional):
   ```bash
   az deployment group what-if \
     --resource-group CleverSyncSOS-rg \
     --template-file infra/main.bicep \
     --parameters prefix=cleversync
   ```
3. **Commit and push** to master
4. Pipeline will apply infrastructure changes

## Costs

The pipeline uses:
- **GitHub Actions:** Free tier includes 2,000 minutes/month for public repos
- **Azure Resources:**
  - Consumption Plan Function App: Pay per execution (1M free/month)
  - Storage: ~$0.02/GB/month
  - Application Insights: First 5GB/month free

## Next Steps

1. **Set up the Azure Service Principal** (see Prerequisites above)
2. **Add GitHub Secrets** (AZURE_CREDENTIALS)
3. **Push to master** and watch the pipeline run
4. **Monitor** the deployment in GitHub Actions and Azure Portal

## Support

For issues with:
- **Pipeline:** Check GitHub Actions logs
- **Azure Resources:** Check Azure Portal → Activity Log
- **Application Errors:** Check Application Insights → Failures
