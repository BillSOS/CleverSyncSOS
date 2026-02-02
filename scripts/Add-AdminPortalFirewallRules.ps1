# Add Admin Portal Firewall Rules
# This script automatically adds all Web App outbound IPs to SQL Server firewall

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$WebAppName,

    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory=$true)]
    [string]$SqlServerName
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Add Admin Portal SQL Firewall Rules" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if logged into Azure
Write-Host "Checking Azure CLI login status..." -ForegroundColor Yellow
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "ERROR: Not logged into Azure CLI. Run 'az login' first." -ForegroundColor Red
    exit 1
}
Write-Host "✓ Logged in as: $($account.user.name)" -ForegroundColor Green
Write-Host ""

# Get Web App outbound IPs
Write-Host "Getting Web App outbound IP addresses..." -ForegroundColor Yellow
$ips = az webapp show `
    --name $WebAppName `
    --resource-group $ResourceGroupName `
    --query "outboundIpAddresses" `
    --output tsv

if (-not $ips) {
    Write-Host "ERROR: Could not retrieve Web App IP addresses." -ForegroundColor Red
    Write-Host "Verify the Web App name and resource group are correct." -ForegroundColor Red
    exit 1
}

# Split by comma
$ipArray = $ips -split ','
Write-Host "✓ Found $($ipArray.Count) IP addresses to add" -ForegroundColor Green
Write-Host ""

# Add each IP to firewall
Write-Host "Adding firewall rules..." -ForegroundColor Yellow
$counter = 1
$successCount = 0
$failCount = 0

foreach ($ip in $ipArray) {
    $ipTrimmed = $ip.Trim()
    $ruleName = "AdminPortal-IP$counter"

    Write-Host "[$counter/$($ipArray.Count)] Adding rule '$ruleName' for $ipTrimmed..." -ForegroundColor Cyan

    try {
        # Check if rule already exists
        $existing = az sql server firewall-rule show `
            --resource-group $ResourceGroupName `
            --server $SqlServerName `
            --name $ruleName `
            --output json 2>$null | ConvertFrom-Json

        if ($existing) {
            Write-Host "  ⚠ Rule already exists, updating..." -ForegroundColor Yellow
        }

        # Create or update the rule
        az sql server firewall-rule create `
            --resource-group $ResourceGroupName `
            --server $SqlServerName `
            --name $ruleName `
            --start-ip-address $ipTrimmed `
            --end-ip-address $ipTrimmed `
            --output none

        Write-Host "  ✓ Success" -ForegroundColor Green
        $successCount++
    }
    catch {
        Write-Host "  ✗ Failed: $_" -ForegroundColor Red
        $failCount++
    }

    $counter++
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Total IPs: $($ipArray.Count)" -ForegroundColor White
Write-Host "Successful: $successCount" -ForegroundColor Green
Write-Host "Failed: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Green" })
Write-Host ""

if ($successCount -gt 0) {
    Write-Host "Verifying firewall rules..." -ForegroundColor Yellow
    az sql server firewall-rule list `
        --resource-group $ResourceGroupName `
        --server $SqlServerName `
        --query "[?contains(name, 'AdminPortal')].{Name:name, StartIP:startIpAddress, EndIP:endIpAddress}" `
        --output table
    Write-Host ""
}

if ($failCount -eq 0) {
    Write-Host "✓ All firewall rules added successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next step: Test your Admin Portal connection" -ForegroundColor Yellow
    Write-Host "https://$WebAppName.azurewebsites.net/admin/bypass-login" -ForegroundColor Cyan
} else {
    Write-Host "⚠ Some firewall rules failed. Please review errors above." -ForegroundColor Yellow
}

Write-Host ""
