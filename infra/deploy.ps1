#!/usr/bin/env pwsh
# Deploy CleverSyncSOS Infrastructure to Azure
# This script deploys the Bicep template to create all required Azure resources

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory=$false)]
    [string]$Location = "eastus",

    [Parameter(Mandatory=$true)]
    [string]$SqlAdminLogin,

    [Parameter(Mandatory=$true)]
    [SecureString]$SqlAdminPassword,

    [Parameter(Mandatory=$false)]
    [string]$Prefix = "cleversync",

    [Parameter(Mandatory=$false)]
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "CleverSyncSOS Infrastructure Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if Azure CLI is installed
try {
    $azVersion = az version --output json | ConvertFrom-Json
    Write-Host "✓ Azure CLI version: $($azVersion.'azure-cli')" -ForegroundColor Green
} catch {
    Write-Error "Azure CLI is not installed. Please install from https://aka.ms/installazurecliwindows"
    exit 1
}

# Check if logged in
Write-Host "Checking Azure login status..." -ForegroundColor Yellow
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Not logged in. Initiating login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "✓ Logged in as: $($account.user.name)" -ForegroundColor Green
Write-Host "✓ Subscription: $($account.name) ($($account.id))" -ForegroundColor Green
Write-Host ""

# Check if resource group exists, create if not
Write-Host "Checking resource group '$ResourceGroupName'..." -ForegroundColor Yellow
$rg = az group show --name $ResourceGroupName --output json 2>$null | ConvertFrom-Json
if (-not $rg) {
    Write-Host "Resource group does not exist. Creating..." -ForegroundColor Yellow
    az group create --name $ResourceGroupName --location $Location --output none
    Write-Host "✓ Resource group created" -ForegroundColor Green
} else {
    Write-Host "✓ Resource group exists" -ForegroundColor Green
}
Write-Host ""

# Convert SecureString to plain text for Azure CLI (temporary)
$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlAdminPassword)
$PlainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)

# Deploy Bicep template
Write-Host "Deploying infrastructure..." -ForegroundColor Yellow
Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor Gray
Write-Host "  Location: $Location" -ForegroundColor Gray
Write-Host "  Prefix: $Prefix" -ForegroundColor Gray
Write-Host "  SQL Admin: $SqlAdminLogin" -ForegroundColor Gray
Write-Host ""

$deploymentName = "cleversync-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

try {
    if ($WhatIf) {
        Write-Host "Running in WhatIf mode (no changes will be made)..." -ForegroundColor Magenta
        az deployment group what-if `
            --resource-group $ResourceGroupName `
            --template-file "$PSScriptRoot/main.bicep" `
            --parameters prefix=$Prefix location=$Location sqlAdminLogin=$SqlAdminLogin sqlAdminPassword=$PlainPassword
    } else {
        $deployment = az deployment group create `
            --resource-group $ResourceGroupName `
            --template-file "$PSScriptRoot/main.bicep" `
            --parameters prefix=$Prefix location=$Location sqlAdminLogin=$SqlAdminLogin sqlAdminPassword=$PlainPassword `
            --name $deploymentName `
            --output json | ConvertFrom-Json

        Write-Host ""
        Write-Host "========================================" -ForegroundColor Green
        Write-Host "Deployment Complete!" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "Outputs:" -ForegroundColor Cyan
        $deployment.properties.outputs.PSObject.Properties | ForEach-Object {
            Write-Host "  $($_.Name): $($_.Value.value)" -ForegroundColor White
        }
        Write-Host ""
        Write-Host "Next Steps:" -ForegroundColor Yellow
        Write-Host "1. Run database migrations to create SessionDb schema" -ForegroundColor White
        Write-Host "   dotnet ef database update --context SessionDbContext" -ForegroundColor Gray
        Write-Host ""
        Write-Host "2. Add Clever API credentials to Key Vault" -ForegroundColor White
        Write-Host "   az keyvault secret set --vault-name <key-vault-name> --name 'CleverSyncSOS--District-<Name>--ClientId' --value '<client-id>'" -ForegroundColor Gray
        Write-Host "   az keyvault secret set --vault-name <key-vault-name> --name 'CleverSyncSOS--District-<Name>--ClientSecret' --value '<client-secret>'" -ForegroundColor Gray
        Write-Host ""
        Write-Host "3. Deploy Function App code" -ForegroundColor White
        Write-Host "   func azure functionapp publish <function-app-name>" -ForegroundColor Gray
    }
} catch {
    Write-Error "Deployment failed: $_"
    exit 1
} finally {
    # Clear password from memory
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
    $PlainPassword = $null
}
