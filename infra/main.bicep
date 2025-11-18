// infra/main.bicep
// FINAL: Deploys a single serverless Function App on a Consumption Plan, avoiding all App Service Plan quota issues.
// Also deploys App Insights, Storage Account, and Key Vault.
// MODIFIED: Targeting .NET 8 for compatibility check.

@description('Deployment location')
param location string = resourceGroup().location

@description('Resource name prefix (e.g., "cleversync")')
param prefix string = 'cleversync'

// Shortened the name significantly to ensure it is under 24 chars.
var storageAccountName = toLower('cs${uniqueString(resourceGroup().id)}')
var appInsightsName = '${prefix}-ai'
var functionAppName = '${prefix}-fn'
var keyVaultName = '${prefix}-kv'

// Application Insights
resource appInsights 'microsoft.insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

// Storage Account for Function App (required)
resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// Function App (on a serverless Consumption Plan)
resource functionApp 'Microsoft.Web/sites@2022-03-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // By omitting 'serverFarmId', a serverless Consumption plan is created.
    siteConfig: {
      // REMOVED: linuxFxVersion is not used for Consumption Plan; runtime is set via app settings.
      appSettings: [
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
      ]
    }
    httpsOnly: true
  }
}

// Use resource symbolic reference to get storage keys
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

// Key Vault (grant secret get/list to the Function App's managed identity)
resource keyVault 'Microsoft.KeyVault/vaults@2022-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: functionApp.identity.principalId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
        }
      }
    ]
    enableSoftDelete: true
    enablePurgeProtection: true
  }
}

// Function App app settings
resource functionAppAppSettings 'Microsoft.Web/sites/config@2022-03-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    // Azure Functions runtime settings
    AzureWebJobsStorage: storageConnectionString
    WEBSITE_CONTENTAZUREFILECONNECTIONSTRING: storageConnectionString
    WEBSITE_CONTENTSHARE: toLower(functionApp.name)
    FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
    FUNCTIONS_EXTENSION_VERSION: '~4'
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
    KEYVAULT_URI: keyVault.properties.vaultUri

    // CleverAuth configuration
    CleverAuth__KeyVaultUri: keyVault.properties.vaultUri
    CleverAuth__TokenEndpoint: 'https://clever.com/oauth/tokens'
    CleverAuth__MaxRetryAttempts: '5'
    CleverAuth__InitialRetryDelaySeconds: '2'
    CleverAuth__TokenRefreshThresholdPercent: '75.0'
    CleverAuth__HttpTimeoutSeconds: '30'

    // CleverApi configuration
    CleverApi__BaseUrl: 'https://api.clever.com/v3.0'
    CleverApi__PageSize: '100'
    CleverApi__MaxRetryAttempts: '5'
    CleverApi__InitialRetryDelaySeconds: '2'

    // Database connection strings (loaded from Key Vault)
    ConnectionStrings__SessionDb: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=SessionDb-ConnectionString)'
  }
}

// Outputs
output functionAppDefaultHostName string = functionApp.properties.defaultHostName
output keyVaultUri string = keyVault.properties.vaultUri
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output storageAccountName string = storageAccount.name