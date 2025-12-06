# Migrate-ToElasticPool.ps1
# Migrates existing Azure SQL databases to the SOS-Pool elastic pool
#
# Prerequisites:
# - Azure CLI installed and logged in (az login)
# - User must have SQL Server Contributor permissions
#
# Usage:
#   .\Migrate-ToElasticPool.ps1 -ResourceGroup "your-rg" -SqlServer "your-server"
#
# Optional Parameters:
#   -ElasticPoolName "SOS-Pool" (default)
#   -WhatIf (preview changes without executing)

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $true)]
    [string]$SqlServer,

    [Parameter(Mandatory = $false)]
    [string]$ElasticPoolName = "SOS-Pool",

    [Parameter(Mandatory = $false)]
    [switch]$DeleteOldResources
)

$ErrorActionPreference = "Stop"

Write-Host "=== CleverSyncSOS Database Migration to Elastic Pool ===" -ForegroundColor Cyan
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

# Check if SQL Server exists
Write-Host "Checking SQL Server '$SqlServer' in resource group '$ResourceGroup'..." -ForegroundColor Yellow
try {
    $server = az sql server show --name $SqlServer --resource-group $ResourceGroup --output json 2>$null | ConvertFrom-Json
    Write-Host "✓ SQL Server found: $($server.fullyQualifiedDomainName)" -ForegroundColor Green
} catch {
    Write-Error "SQL Server '$SqlServer' not found in resource group '$ResourceGroup'"
    exit 1
}

# Check if elastic pool exists
Write-Host "Checking elastic pool '$ElasticPoolName'..." -ForegroundColor Yellow
try {
    $pool = az sql elastic-pool show --name $ElasticPoolName --server $SqlServer --resource-group $ResourceGroup --output json 2>$null | ConvertFrom-Json
    Write-Host "✓ Elastic pool found: $($pool.name)" -ForegroundColor Green
    Write-Host "  - Tier: $($pool.sku.tier)" -ForegroundColor Gray
    Write-Host "  - Capacity: $($pool.sku.capacity) eDTU" -ForegroundColor Gray
    Write-Host "  - Max Size: $([math]::Round($pool.maxSizeBytes / 1GB, 2)) GB" -ForegroundColor Gray
    Write-Host ""
} catch {
    Write-Error "Elastic pool '$ElasticPoolName' not found. Please deploy the infrastructure first using main.bicep"
    exit 1
}

# Get all databases on the server
Write-Host "Retrieving databases on server '$SqlServer'..." -ForegroundColor Yellow
try {
    $databases = az sql db list --server $SqlServer --resource-group $ResourceGroup --output json | ConvertFrom-Json
    Write-Host "✓ Found $($databases.Count) database(s)" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Error "Failed to retrieve databases from SQL Server"
    exit 1
}

# Filter databases that need migration
$databasesToMigrate = $databases | Where-Object {
    # Skip master database
    $_.name -ne "master" -and
    # Only migrate databases not already in the elastic pool
    $_.elasticPoolId -eq $null
}

if ($databasesToMigrate.Count -eq 0) {
    Write-Host "✓ All databases are already in elastic pools. No migration needed." -ForegroundColor Green
    exit 0
}

Write-Host "Databases to migrate:" -ForegroundColor Cyan
foreach ($db in $databasesToMigrate) {
    $currentTier = if ($db.currentSku) { "$($db.currentSku.tier) - $($db.currentSku.name)" } else { "Unknown" }
    Write-Host "  - $($db.name) (Current: $currentTier)" -ForegroundColor White
}
Write-Host ""

# Confirm migration
if (-not $PSCmdlet.ShouldProcess("Migrate $($databasesToMigrate.Count) database(s) to elastic pool '$ElasticPoolName'", "Database Migration", "Migrate databases")) {
    Write-Host "Migration cancelled by user." -ForegroundColor Yellow
    exit 0
}

# Perform migration
Write-Host "=== Starting Migration ===" -ForegroundColor Cyan
Write-Host ""

$successCount = 0
$failureCount = 0
$results = @()

foreach ($db in $databasesToMigrate) {
    Write-Host "Migrating '$($db.name)' to elastic pool '$ElasticPoolName'..." -ForegroundColor Yellow

    try {
        # Migrate database to elastic pool
        $result = az sql db update `
            --name $db.name `
            --server $SqlServer `
            --resource-group $ResourceGroup `
            --elastic-pool $ElasticPoolName `
            --output json | ConvertFrom-Json

        Write-Host "  ✓ Migration successful" -ForegroundColor Green
        $successCount++

        $results += [PSCustomObject]@{
            Database = $db.name
            Status = "Success"
            OldTier = if ($db.currentSku) { "$($db.currentSku.tier)" } else { "Unknown" }
            NewTier = "ElasticPool"
        }
    } catch {
        Write-Host "  ✗ Migration failed: $_" -ForegroundColor Red
        $failureCount++

        $results += [PSCustomObject]@{
            Database = $db.name
            Status = "Failed"
            OldTier = if ($db.currentSku) { "$($db.currentSku.tier)" } else { "Unknown" }
            Error = $_.Exception.Message
        }
    }

    Write-Host ""
}

# Summary
Write-Host "=== Migration Summary ===" -ForegroundColor Cyan
Write-Host "Total databases processed: $($databasesToMigrate.Count)" -ForegroundColor White
Write-Host "Successful migrations: $successCount" -ForegroundColor Green
Write-Host "Failed migrations: $failureCount" -ForegroundColor $(if ($failureCount -gt 0) { "Red" } else { "Gray" })
Write-Host ""

# Display results table
$results | Format-Table -AutoSize

if ($failureCount -gt 0) {
    Write-Host "⚠ Some databases failed to migrate. Please review errors above." -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "✓ All databases successfully migrated to elastic pool '$ElasticPoolName'" -ForegroundColor Green
    exit 0
}
