# Grant Key Vault access to CleverSyncSOS App Service Managed Identity

$managedIdentityObjectId = "3cb9a608-eff9-448e-8563-ac355c7d93cf"
$keyVaultName = "cleversync-kv"
$subscriptionId = "ba43c26e-f191-40ca-87ae-96a8df70c593"
$resourceGroup = "CleverSyncSOS-rg"

Write-Host "Granting Key Vault Secrets User role to CleverSyncSOS App Service..." -ForegroundColor Cyan

$keyVaultResourceId = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.KeyVault/vaults/$keyVaultName"

try {
    az role assignment create `
        --role "Key Vault Secrets User" `
        --assignee-object-id $managedIdentityObjectId `
        --assignee-principal-type ServicePrincipal `
        --scope $keyVaultResourceId

    Write-Host "✓ Successfully granted access!" -ForegroundColor Green
    Write-Host "`nNow restarting the App Service..." -ForegroundColor Cyan

    az webapp restart --name CleverSyncSOS --resource-group SOSConsolidated

    Write-Host "✓ App Service restarted!" -ForegroundColor Green
    Write-Host "`nPlease wait 30-60 seconds, then refresh your browser at:" -ForegroundColor Yellow
    Write-Host "https://cleversyncsos.azurewebsites.net/" -ForegroundColor Cyan
}
catch {
    Write-Host "✗ Error occurred:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "`nPlease grant access manually via Azure Portal:" -ForegroundColor Yellow
    Write-Host "1. Go to https://portal.azure.com" -ForegroundColor White
    Write-Host "2. Navigate to Key Vault 'cleversync-kv'" -ForegroundColor White
    Write-Host "3. Click 'Access control (IAM)' → '+ Add' → 'Add role assignment'" -ForegroundColor White
    Write-Host "4. Select role: 'Key Vault Secrets User'" -ForegroundColor White
    Write-Host "5. Select member: 'CleverSyncSOS' (the App Service)" -ForegroundColor White
    Write-Host "6. Click 'Review + assign'" -ForegroundColor White
}
