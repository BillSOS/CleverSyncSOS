# CleverSync Admin Portal Deployment Script
# This script deploys the Admin Portal to Azure App Service

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory=$true)]
    [string]$WebAppName,

    [Parameter(Mandatory=$true)]
    [string]$KeyVaultName,

    [Parameter(Mandatory=$false)]
    [string]$Location = "eastus",

    [Parameter(Mandatory=$false)]
    [switch]$SkipBuild,

    [Parameter(Mandatory=$false)]
    [switch]$SkipInfrastructure
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "CleverSync Admin Portal Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ==========================================================================
# Step 1: Prerequisites Check
# ==========================================================================

Write-Host "[Step 1/7] Checking prerequisites..." -ForegroundColor Yellow

# Check if logged into Azure
Write-Host "Checking Azure CLI login status..."
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "ERROR: Not logged into Azure CLI. Run 'az login' first." -ForegroundColor Red
    exit 1
}
Write-Host "  Logged in as: $($account.user.name)" -ForegroundColor Green
Write-Host "  Subscription: $($account.name)" -ForegroundColor Green

# Check if .NET SDK is installed
Write-Host "Checking .NET SDK..."
$dotnetVersion = dotnet --version 2>$null
if (-not $dotnetVersion) {
    Write-Host "ERROR: .NET SDK not found. Please install .NET 9 SDK." -ForegroundColor Red
    exit 1
}
Write-Host "  .NET SDK version: $dotnetVersion" -ForegroundColor Green

Write-Host ""

# ==========================================================================
# Step 2: Build Application
# ==========================================================================

if (-not $SkipBuild) {
    Write-Host "[Step 2/7] Building application..." -ForegroundColor Yellow

    $projectPath = Join-Path $PSScriptRoot "..\src\CleverSyncSOS.AdminPortal"
    $publishPath = Join-Path $projectPath "publish"

    # Clean previous publish
    if (Test-Path $publishPath) {
        Remove-Item -Path $publishPath -Recurse -Force
    }

    # Build and publish
    Write-Host "Publishing application..."
    Push-Location $projectPath
    try {
        dotnet publish -c Release -o publish
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE"
        }
        Write-Host "  Build successful" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }

    # Create zip package
    Write-Host "Creating deployment package..."
    $zipPath = Join-Path $projectPath "adminportal.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Push-Location (Join-Path $projectPath "publish")
    try {
        Compress-Archive -Path * -DestinationPath $zipPath
        Write-Host "  Package created: $zipPath" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }

    Write-Host ""
} else {
    Write-Host "[Step 2/7] Skipping build (using existing package)..." -ForegroundColor Yellow
    Write-Host ""
}

# ==========================================================================
# Step 3: Create Infrastructure
# ==========================================================================

if (-not $SkipInfrastructure) {
    Write-Host "[Step 3/7] Creating Azure infrastructure..." -ForegroundColor Yellow

    # Create resource group
    Write-Host "Creating resource group..."
    az group create --name $ResourceGroupName --location $Location --output none
    Write-Host "  Resource group: $ResourceGroupName" -ForegroundColor Green

    # Create App Service Plan
    $appServicePlan = "${WebAppName}-plan"
    Write-Host "Creating App Service Plan..."
    az appservice plan create `
        --name $appServicePlan `
        --resource-group $ResourceGroupName `
        --sku B1 `
        --is-linux `
        --output none
    Write-Host "  App Service Plan: $appServicePlan" -ForegroundColor Green

    # Create Web App
    Write-Host "Creating Web App..."
    az webapp create `
        --name $WebAppName `
        --resource-group $ResourceGroupName `
        --plan $appServicePlan `
        --runtime "DOTNET:9.0" `
        --output none
    Write-Host "  Web App: $WebAppName" -ForegroundColor Green

    Write-Host ""
} else {
    Write-Host "[Step 3/7] Skipping infrastructure creation..." -ForegroundColor Yellow
    Write-Host ""
}

# ==========================================================================
# Step 4: Configure Managed Identity
# ==========================================================================

Write-Host "[Step 4/7] Configuring managed identity..." -ForegroundColor Yellow

# Enable system-assigned managed identity
Write-Host "Enabling managed identity..."
az webapp identity assign `
    --name $WebAppName `
    --resource-group $ResourceGroupName `
    --output none

# Get principal ID
$principalId = az webapp identity show `
    --name $WebAppName `
    --resource-group $ResourceGroupName `
    --query principalId `
    --output tsv

Write-Host "  Principal ID: $principalId" -ForegroundColor Green

# Grant Key Vault access
Write-Host "Granting Key Vault access..."
az keyvault set-policy `
    --name $KeyVaultName `
    --object-id $principalId `
    --secret-permissions get list `
    --output none
Write-Host "  Key Vault access granted" -ForegroundColor Green

Write-Host ""

# ==========================================================================
# Step 5: Configure Application Settings
# ==========================================================================

Write-Host "[Step 5/7] Configuring application settings..." -ForegroundColor Yellow

# Get Key Vault URI
$keyVaultUri = "https://$KeyVaultName.vault.azure.net/"

# Get Clever Client ID (assuming it's stored in environment or prompt)
Write-Host "Enter your Clever OAuth Client ID:"
$cleverClientId = Read-Host

# Set application settings
Write-Host "Applying application settings..."
az webapp config appsettings set `
    --name $WebAppName `
    --resource-group $ResourceGroupName `
    --settings `
        AzureKeyVault__VaultUri=$keyVaultUri `
        Clever__ClientId=$cleverClientId `
    --output none

Write-Host "  Application settings configured" -ForegroundColor Green

# Enable HTTPS only
Write-Host "Enabling HTTPS only..."
az webapp update `
    --name $WebAppName `
    --resource-group $ResourceGroupName `
    --https-only true `
    --output none
Write-Host "  HTTPS enforced" -ForegroundColor Green

Write-Host ""

# ==========================================================================
# Step 6: Deploy Application
# ==========================================================================

Write-Host "[Step 6/7] Deploying application..." -ForegroundColor Yellow

$projectPath = Join-Path $PSScriptRoot "..\src\CleverSyncSOS.AdminPortal"
$zipPath = Join-Path $projectPath "adminportal.zip"

if (-not (Test-Path $zipPath)) {
    Write-Host "ERROR: Deployment package not found: $zipPath" -ForegroundColor Red
    Write-Host "Run without -SkipBuild to create the package." -ForegroundColor Red
    exit 1
}

Write-Host "Uploading and deploying package..."
az webapp deployment source config-zip `
    --name $WebAppName `
    --resource-group $ResourceGroupName `
    --src $zipPath `
    --output none

Write-Host "  Deployment complete" -ForegroundColor Green

Write-Host ""

# ==========================================================================
# Step 7: Post-Deployment Configuration
# ==========================================================================

Write-Host "[Step 7/7] Post-deployment configuration..." -ForegroundColor Yellow

# Get the app URL
$appUrl = "https://$WebAppName.azurewebsites.net"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deployment Complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Application URL: $appUrl" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Update your Clever OAuth application:" -ForegroundColor White
Write-Host "   Redirect URI: $appUrl/signin-clever" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Verify the following secrets exist in Key Vault:" -ForegroundColor White
Write-Host "   - Clever-OAuth-ClientSecret" -ForegroundColor Gray
Write-Host "   - SuperAdmin-Password" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Test the application:" -ForegroundColor White
Write-Host "   Login: $appUrl/login" -ForegroundColor Gray
Write-Host "   Bypass: $appUrl/admin/bypass-login" -ForegroundColor Gray
Write-Host ""
Write-Host "4. View logs:" -ForegroundColor White
Write-Host "   az webapp log tail --name $WebAppName --resource-group $ResourceGroupName" -ForegroundColor Gray
Write-Host ""
Write-Host "For detailed setup instructions, see:" -ForegroundColor Yellow
Write-Host "docs/AdminPortal-Deployment-Guide.md" -ForegroundColor Gray
Write-Host ""
