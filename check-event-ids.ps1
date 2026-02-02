# Check for LastEventId in SyncHistory
$server = "sos-northcentral.database.windows.net"
$database = "SessionDb"

# Get connection string from Azure
$connStr = az sql db show-connection-string --server sos-northcentral --name SessionDb --client ado.net --auth-type SqlPassword | ConvertFrom-Json
$connStr = $connStr -replace '<username>', 'cleversyncadmin' -replace '<password>', (az keyvault secret show --vault-name cleversync-kv --name SessionDb-AdminPassword --query value -o tsv)

# Query for LastEventId
$query = @"
SELECT TOP 10
    SyncId,
    SchoolId,
    EntityType,
    SyncType,
    Status,
    RecordsProcessed,
    LastEventId,
    LastSyncTimestamp,
    SyncEndTime
FROM SyncHistory
ORDER BY SyncEndTime DESC
"@

Write-Host "=== CHECKING FOR LAST EVENT IDs ===" -ForegroundColor Cyan
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connStr)
    $connection.Open()

    $command = $connection.CreateCommand()
    $command.CommandText = $query
    $reader = $command.ExecuteReader()

    while ($reader.Read()) {
        Write-Host "SyncId: $($reader['SyncId'])" -ForegroundColor Yellow
        Write-Host "  SchoolId: $($reader['SchoolId'])"
        Write-Host "  EntityType: $($reader['EntityType'])"
        Write-Host "  SyncType: $($reader['SyncType']) (1=Full, 2=Incremental)"
        Write-Host "  Status: $($reader['Status'])"
        Write-Host "  RecordsProcessed: $($reader['RecordsProcessed'])"
        Write-Host "  LastEventId: $($reader['LastEventId'])" -ForegroundColor $(if ($reader['LastEventId'] -eq [DBNull]::Value) { 'Red' } else { 'Green' })
        Write-Host "  LastSyncTimestamp: $($reader['LastSyncTimestamp'])"
        Write-Host "  SyncEndTime: $($reader['SyncEndTime'])"
        Write-Host ""
    }

    $reader.Close()
    $connection.Close()

    Write-Host "Query complete!" -ForegroundColor Green
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
}
