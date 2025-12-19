# Deploy CleverSyncSOS Admin Portal to Azure
# Resource Group: SOSConsolidated
# App Service: CleverSyncSOS

$ErrorActionPreference = "Stop"

$projectPath = "c:\Users\BillM\source\repos\CleverSyncSOS\src\CleverSyncSOS.AdminPortal"
$publishPath = "$projectPath\publish-output"
$zipPath = "$projectPath\adminportal-deploy.zip"

Write-Host "Building and publishing Admin Portal..." -ForegroundColor Cyan

# Clean and publish
if (Test-Path $publishPath) {
    Remove-Item -Path $publishPath -Recurse -Force
}

dotnet publish "$projectPath\CleverSyncSOS.AdminPortal.csproj" -c Release -o $publishPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed"
    exit 1
}

Write-Host "Creating deployment zip..." -ForegroundColor Cyan

# Remove old zip if exists
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

# Create zip
Compress-Archive -Path "$publishPath\*" -DestinationPath $zipPath -Force

Write-Host "Deploying to Azure..." -ForegroundColor Cyan

# Deploy to Azure
az webapp deploy --resource-group SOSConsolidated --name CleverSyncSOS --src-path $zipPath --type zip --restart true

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nDeployment successful!" -ForegroundColor Green
    Write-Host "App URL: https://cleversyncsos.azurewebsites.net" -ForegroundColor Green
} else {
    Write-Error "Deployment failed"
    exit 1
}
