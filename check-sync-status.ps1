# Check recent SyncHistory entries
$password = az keyvault secret show --vault-name cleversync-kv --name SessionDbPassword --query value -o tsv
$connStr = "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;Persist Security Info=False;User ID=cleversyncadmin;Password=$password;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

$query = @"
SELECT TOP 5
    SyncId,
    SchoolId,
    EntityType,
    SyncType,
    Status,
    RecordsProcessed,
    RecordsUpdated,
    LastEventId,
    SyncStartTime,
    SyncEndTime
FROM SyncHistory
ORDER BY SyncEndTime DESC
"@

Write-Host "=== RECENT SYNC HISTORY ===" -ForegroundColor Cyan
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connStr)
    $connection.Open()

    $command = $connection.CreateCommand()
    $command.CommandText = $query
    $reader = $command.ExecuteReader()

    $count = 0
    while ($reader.Read()) {
        $count++
        Write-Host "[$count] SyncId: $($reader['SyncId'])" -ForegroundColor Yellow
        Write-Host "    SchoolId: $($reader['SchoolId'])"
        Write-Host "    EntityType: $($reader['EntityType'])"
        Write-Host "    SyncType: $($reader['SyncType'])"
        Write-Host "    Status: $($reader['Status'])" -ForegroundColor $(if ($reader['Status'] -eq 'Success') { 'Green' } else { 'Red' })
        Write-Host "    RecordsProcessed: $($reader['RecordsProcessed'])"
        Write-Host "    RecordsUpdated: $($reader['RecordsUpdated'])"

        $lastEventId = $reader['LastEventId']
        if ($lastEventId -eq [DBNull]::Value) {
            Write-Host "    LastEventId: NULL" -ForegroundColor Red
        } else {
            Write-Host "    LastEventId: $lastEventId" -ForegroundColor Green
        }

        Write-Host "    SyncStartTime: $($reader['SyncStartTime'])"
        Write-Host "    SyncEndTime: $($reader['SyncEndTime'])"
        Write-Host ""
    }

    if ($count -eq 0) {
        Write-Host "No sync history found" -ForegroundColor Yellow
    }

    $reader.Close()
    $connection.Close()

    Write-Host "Query complete!" -ForegroundColor Green
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
