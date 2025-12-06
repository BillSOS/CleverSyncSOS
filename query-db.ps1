$connectionString = "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SessionAdmin;Password=PoMS^f3Q6I%Y;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString

try {
    $connection.Open()
    Write-Host "Connection successful!" -ForegroundColor Green

    # Query Districts
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT DistrictId, CleverDistrictId, Name, KeyVaultDistrictPrefix FROM Districts"
    $reader = $command.ExecuteReader()

    Write-Host "`n=== DISTRICTS ===" -ForegroundColor Cyan
    while ($reader.Read()) {
        Write-Host "DistrictId: $($reader['DistrictId']), CleverDistrictId: $($reader['CleverDistrictId']), Name: $($reader['Name']), KeyVaultPrefix: $($reader['KeyVaultDistrictPrefix'])"
    }
    $reader.Close()

    # Query Schools
    $command.CommandText = "SELECT SchoolId, DistrictId, CleverSchoolId, Name, KeyVaultSchoolPrefix, DatabaseName, IsActive, RequiresFullSync FROM Schools"
    $reader = $command.ExecuteReader()

    Write-Host "`n=== SCHOOLS ===" -ForegroundColor Cyan
    while ($reader.Read()) {
        Write-Host "SchoolId: $($reader['SchoolId']), Name: $($reader['Name']), KeyVaultPrefix: $($reader['KeyVaultSchoolPrefix']), DatabaseName: $($reader['DatabaseName']), IsActive: $($reader['IsActive'])"
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
