$connectionString = "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SessionAdmin;Password=PoMS^f3Q6I%Y;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString

try {
    $connection.Open()
    Write-Host "Connection successful!" -ForegroundColor Green

    # Query Schools table columns
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Schools' ORDER BY ORDINAL_POSITION"
    $reader = $command.ExecuteReader()

    Write-Host "`n=== SCHOOLS TABLE COLUMNS ===" -ForegroundColor Cyan
    while ($reader.Read()) {
        Write-Host "$($reader['COLUMN_NAME']) ($($reader['DATA_TYPE']))"
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
