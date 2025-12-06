$connectionString = "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SessionAdmin;Password=PoMS^f3Q6I%Y;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString

try {
    $connection.Open()
    Write-Host "Connection successful!" -ForegroundColor Green

    # Query SyncHistory
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT TOP 10 * FROM SyncHistory ORDER BY SyncStartTime DESC"
    $reader = $command.ExecuteReader()

    Write-Host "`n=== SYNC HISTORY (Recent 10) ===" -ForegroundColor Cyan
    while ($reader.Read()) {
        Write-Host "`nSyncId: $($reader['SyncId'])"
        Write-Host "SchoolId: $($reader['SchoolId'])"
        Write-Host "EntityType: $($reader['EntityType'])"
        Write-Host "SyncType: $($reader['SyncType'])"
        Write-Host "Status: $($reader['Status'])"
        Write-Host "SyncStartTime: $($reader['SyncStartTime'])"
        Write-Host "SyncEndTime: $($reader['SyncEndTime'])"
        Write-Host "RecordsProcessed: $($reader['RecordsProcessed'])"
        Write-Host "RecordsFailed: $($reader['RecordsFailed'])"
        Write-Host "ErrorMessage: $($reader['ErrorMessage'])"
    }
    $reader.Close()

    $connection.Close()
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
}
finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
}
