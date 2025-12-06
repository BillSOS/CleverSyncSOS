# Migrate-KeyVaultSecrets.ps1
# Migrates Key Vault secrets to the new standardized naming convention
#
# Prerequisites:
# - Azure CLI installed and logged in (az login)
# - User must have Key Vault Secrets Officer or equivalent permissions
#
# Usage:
#   .\Migrate-KeyVaultSecrets.ps1 -KeyVaultName "your-keyvault"
#
# Optional Parameters:
#   -DeleteOldSecrets (removes old secrets after successful migration)
#   -WhatIf (preview changes without executing)
#
# New Naming Convention: CleverSyncSOS--{Component}--{Property}
# Examples:
#   SessionDb-ConnectionString -> CleverSyncSOS--SessionDb--ConnectionString
#   CleverClientId -> CleverSyncSOS--Clever--ClientId
#   CleverClientSecret -> CleverSyncSOS--Clever--ClientSecret

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$KeyVaultName,

    [Parameter(Mandatory = $false)]
    [switch]$DeleteOldSecrets
)

$ErrorActionPreference = "Stop"

Write-Host "=== CleverSyncSOS Key Vault Secret Migration ===" -ForegroundColor Cyan
Write-Host ""

# Verify Azure CLI is installed
try {
    $azVersion = az version --output json 2>$null | ConvertFrom-Json
    Write-Host "✓ Azure CLI version: $($azVersion.'azure-cli')" -ForegroundColor Green
} catch {
    Write-Error "Azure CLI is not installed or not in PATH. Please install from https://aka.ms/installazurecliwindows"
    exit 1
}

# Verify logged in
try {
    $account = az account show --output json 2>$null | ConvertFrom-Json
    Write-Host "✓ Logged in as: $($account.user.name)" -ForegroundColor Green
    Write-Host "✓ Subscription: $($account.name) ($($account.id))" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Error "Not logged in to Azure. Please run 'az login' first."
    exit 1
}

# Check if Key Vault exists and user has access
Write-Host "Checking Key Vault '$KeyVaultName'..." -ForegroundColor Yellow
try {
    $vault = az keyvault show --name $KeyVaultName --output json 2>$null | ConvertFrom-Json
    Write-Host "✓ Key Vault found: $($vault.properties.vaultUri)" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Error "Key Vault '$KeyVaultName' not found or you don't have access. Please check the name and your permissions."
    exit 1
}

# Define migration mapping
# Maps old secret names to new standardized names
$migrationMap = @{
    # SessionDb connection string
    "SessionDb-ConnectionString" = "CleverSyncSOS--SessionDb--ConnectionString"
    "SessionDbConnectionString" = "CleverSyncSOS--SessionDb--ConnectionString"

    # Clever API credentials
    "CleverClientId" = "CleverSyncSOS--Clever--ClientId"
    "Clever-ClientId" = "CleverSyncSOS--Clever--ClientId"
    "CleverClientSecret" = "CleverSyncSOS--Clever--ClientSecret"
    "Clever-ClientSecret" = "CleverSyncSOS--Clever--ClientSecret"
    "CleverAccessToken" = "CleverSyncSOS--Clever--AccessToken"
    "Clever-AccessToken" = "CleverSyncSOS--Clever--AccessToken"

    # Admin Portal
    "SuperAdminPassword" = "CleverSyncSOS--AdminPortal--SuperAdminPassword"
    "AdminPortal-SuperAdminPassword" = "CleverSyncSOS--AdminPortal--SuperAdminPassword"
}

# Get all secrets from Key Vault
Write-Host "Retrieving secrets from Key Vault..." -ForegroundColor Yellow
try {
    $allSecrets = az keyvault secret list --vault-name $KeyVaultName --output json | ConvertFrom-Json
    Write-Host "✓ Found $($allSecrets.Count) secret(s) in Key Vault" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Error "Failed to retrieve secrets from Key Vault. Please check your permissions."
    exit 1
}

# Filter secrets that need migration
$secretsToMigrate = @()
$alreadyMigrated = @()

foreach ($secret in $allSecrets) {
    $secretName = $secret.name

    # Check if this secret needs migration
    if ($migrationMap.ContainsKey($secretName)) {
        $newName = $migrationMap[$secretName]

        # Check if new secret already exists
        $newSecretExists = $allSecrets | Where-Object { $_.name -eq $newName }

        if ($newSecretExists) {
            $alreadyMigrated += [PSCustomObject]@{
                OldName = $secretName
                NewName = $newName
                Status = "Already migrated"
            }
        } else {
            $secretsToMigrate += [PSCustomObject]@{
                OldName = $secretName
                NewName = $newName
            }
        }
    }
}

# Display already migrated secrets
if ($alreadyMigrated.Count -gt 0) {
    Write-Host "Secrets already using new naming convention:" -ForegroundColor Green
    $alreadyMigrated | Format-Table -AutoSize
    Write-Host ""
}

# Check if there are secrets to migrate
if ($secretsToMigrate.Count -eq 0) {
    Write-Host "✓ No secrets need migration. All secrets are already using the standardized naming convention." -ForegroundColor Green
    exit 0
}

# Display migration plan
Write-Host "Secrets to migrate:" -ForegroundColor Cyan
$secretsToMigrate | Format-Table -AutoSize
Write-Host ""

# Confirm migration
$confirmMessage = "Migrate $($secretsToMigrate.Count) secret(s) to new naming convention"
if ($DeleteOldSecrets) {
    $confirmMessage += " and delete old secrets"
}

if (-not $PSCmdlet.ShouldProcess($confirmMessage, "Key Vault Secret Migration", "Migrate secrets")) {
    Write-Host "Migration cancelled by user." -ForegroundColor Yellow
    exit 0
}

# Perform migration
Write-Host "=== Starting Migration ===" -ForegroundColor Cyan
Write-Host ""

$successCount = 0
$failureCount = 0
$results = @()

foreach ($item in $secretsToMigrate) {
    Write-Host "Migrating '$($item.OldName)' -> '$($item.NewName)'..." -ForegroundColor Yellow

    try {
        # Get the old secret value
        $secretValue = az keyvault secret show --vault-name $KeyVaultName --name $item.OldName --query value -o tsv

        if ([string]::IsNullOrWhiteSpace($secretValue)) {
            throw "Secret value is empty or null"
        }

        # Create new secret with standardized name
        $result = az keyvault secret set `
            --vault-name $KeyVaultName `
            --name $item.NewName `
            --value $secretValue `
            --output json | ConvertFrom-Json

        Write-Host "  ✓ New secret created: $($item.NewName)" -ForegroundColor Green

        # Delete old secret if requested
        if ($DeleteOldSecrets) {
            az keyvault secret delete --vault-name $KeyVaultName --name $item.OldName --output none
            Write-Host "  ✓ Old secret deleted: $($item.OldName)" -ForegroundColor Gray
        }

        $successCount++
        $results += [PSCustomObject]@{
            OldName = $item.OldName
            NewName = $item.NewName
            Status = "Success"
            OldDeleted = $DeleteOldSecrets
        }
    } catch {
        Write-Host "  ✗ Migration failed: $_" -ForegroundColor Red
        $failureCount++

        $results += [PSCustomObject]@{
            OldName = $item.OldName
            NewName = $item.NewName
            Status = "Failed"
            Error = $_.Exception.Message
        }
    }

    Write-Host ""
}

# Summary
Write-Host "=== Migration Summary ===" -ForegroundColor Cyan
Write-Host "Total secrets processed: $($secretsToMigrate.Count)" -ForegroundColor White
Write-Host "Successful migrations: $successCount" -ForegroundColor Green
Write-Host "Failed migrations: $failureCount" -ForegroundColor $(if ($failureCount -gt 0) { "Red" } else { "Gray" })
Write-Host ""

# Display results table
$results | Format-Table -AutoSize

# Additional instructions
if ($successCount -gt 0) {
    Write-Host "=== Next Steps ===" -ForegroundColor Cyan
    Write-Host ""

    if (-not $DeleteOldSecrets) {
        Write-Host "⚠ Old secrets are still present in Key Vault" -ForegroundColor Yellow
        Write-Host "  After verifying the application works with new secrets, run this script with -DeleteOldSecrets to clean up" -ForegroundColor Yellow
        Write-Host ""
    }

    Write-Host "1. Update application configuration to reference new secret names" -ForegroundColor White
    Write-Host "2. Restart any applications/services using these secrets" -ForegroundColor White
    Write-Host "3. Verify applications are working correctly" -ForegroundColor White

    if (-not $DeleteOldSecrets) {
        Write-Host "4. Run migration script again with -DeleteOldSecrets flag to remove old secrets" -ForegroundColor White
    }

    Write-Host ""
}

if ($failureCount -gt 0) {
    Write-Host "⚠ Some secrets failed to migrate. Please review errors above." -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "✓ All secrets successfully migrated to standardized naming convention" -ForegroundColor Green
    exit 0
}
