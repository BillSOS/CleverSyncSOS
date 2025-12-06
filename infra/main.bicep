// infra/main.bicep
// FINAL: Deploys a single serverless Function App on a Consumption Plan, avoiding all App Service Plan quota issues.
// Also deploys App Insights, Storage Account, and Key Vault.
// MODIFIED: Targeting .NET 8 for compatibility check.

@description('Deployment location')
param location string = resourceGroup().location

@description('Resource name prefix (e.g., "cleversync")')
param prefix string = 'cleversync'

@description('Environment name (e.g., "dev", "staging", "prod")')
param environment string = 'prod'

@description('SQL Server administrator login')
@secure()
param sqlAdminLogin string

@description('SQL Server administrator password')
@secure()
param sqlAdminPassword string

// Shortened the name significantly to ensure it is under 24 chars.
var storageAccountName = toLower('cs${uniqueString(resourceGroup().id)}')
var appInsightsName = '${prefix}-ai'
var functionAppName = '${prefix}-fn'
var keyVaultName = '${prefix}-kv'
var sqlServerName = '${prefix}-sql-${uniqueString(resourceGroup().id)}'
var sessionDbName = 'SessionDb'

// Common resource tags
var commonTags = {
  Product: 'CleverSyncSOS'
  Environment: environment
  ManagedBy: 'Bicep'
  Repository: 'CleverSyncSOS'
}

// Application Insights
resource appInsights 'microsoft.insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: commonTags
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
  tags: commonTags
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// Azure SQL Server
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  tags: commonTags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Elastic Pool for all databases
resource elasticPool 'Microsoft.Sql/servers/elasticPools@2023-05-01-preview' = {
  parent: sqlServer
  name: 'SOS-Pool'
  location: location
  tags: commonTags
  sku: {
    name: 'StandardPool'
    tier: 'Standard'
    capacity: 50 // 50 eDTUs (can scale up to 3000)
  }
  properties: {
    perDatabaseSettings: {
      minCapacity: 0
      maxCapacity: 50
    }
    maxSizeBytes: 53687091200 // 50 GB
    zoneRedundant: false
  }
}

// SessionDb Database (in elastic pool)
resource sessionDb 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: sessionDbName
  location: location
  tags: commonTags
  sku: {
    name: 'ElasticPool'
    tier: 'Standard'
  }
  properties: {
    elasticPoolId: elasticPool.id
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    catalogCollation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

// SQL Server Firewall Rule - Allow Azure Services
resource sqlFirewallAzureServices 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Function App (on a serverless Consumption Plan)
resource functionApp 'Microsoft.Web/sites@2022-03-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  tags: commonTags
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
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${az.environment().suffixes.storage}'

// SQL Server connection string for SessionDb
var sessionDbConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sessionDbName};Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

// Key Vault (grant secret get/list to the Function App's managed identity)
resource keyVault 'Microsoft.KeyVault/vaults@2022-07-01' = {
  name: keyVaultName
  location: location
  tags: commonTags
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

// Key Vault Secrets - Standardized Naming Convention
// Format: CleverSyncSOS--{Component}--{Property}

// SessionDb Connection String
resource sessionDbSecret 'Microsoft.KeyVault/vaults/secrets@2022-07-01' = {
  parent: keyVault
  name: 'CleverSyncSOS--SessionDb--ConnectionString'
  properties: {
    value: sessionDbConnectionString
  }
}

// Note: Clever credentials and school connection strings should be added manually
// or via separate deployment scripts:
// - CleverSyncSOS--Clever--ClientId
// - CleverSyncSOS--Clever--ClientSecret
// - CleverSyncSOS--Clever--AccessToken (optional, for pre-generated tokens)
// - CleverSyncSOS--AdminPortal--SuperAdminPassword
// - CleverSyncSOS--{SchoolPrefix}--ConnectionString (per school)

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

    // Azure Key Vault configuration
    AzureKeyVault__VaultUri: keyVault.properties.vaultUri

    // CleverAuth configuration - uses standardized secret naming
    CleverAuth__KeyVaultUri: keyVault.properties.vaultUri
    CleverAuth__ClientIdSecretName: 'CleverSyncSOS--Clever--ClientId'
    CleverAuth__ClientSecretSecretName: 'CleverSyncSOS--Clever--ClientSecret'
    CleverAuth__TokenEndpoint: 'https://clever.com/oauth/tokens'
    CleverAuth__MaxRetryAttempts: '5'
    CleverAuth__InitialRetryDelaySeconds: '2'
    CleverAuth__TokenRefreshThresholdPercent: '75.0'
    CleverAuth__HttpTimeoutSeconds: '30'

    // CleverApi configuration
    CleverApi__BaseUrl: 'https://api.clever.com/v3.0/'
    CleverApi__PageSize: '100'
    CleverApi__MaxRetryAttempts: '5'
    CleverApi__InitialRetryDelaySeconds: '2'

    // Database connection strings (loaded from Key Vault with standardized naming)
    ConnectionStrings__SessionDb: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=CleverSyncSOS--SessionDb--ConnectionString)'
  }
}

// Outputs
output functionAppDefaultHostName string = functionApp.properties.defaultHostName
output functionAppPrincipalId string = functionApp.identity.principalId
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultName string = keyVault.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
output storageAccountName string = storageAccount.name
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output elasticPoolName string = elasticPool.name
output elasticPoolCapacity int = elasticPool.sku.capacity
output sessionDbName string = sessionDbName