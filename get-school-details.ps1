$connectionString = "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SessionAdmin;Password=PoMS^f3Q6I%Y;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString

try {
    $connection.Open()
    Write-Host "Connection successful!" -ForegroundColor Green

    # Query Schools with all details
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT SchoolId, DistrictId, CleverSchoolId, Name, KeyVaultSchoolPrefix, DatabaseName, IsActive, RequiresFullSync FROM Schools WHERE SchoolId = 3"
    $reader = $command.ExecuteReader()

    Write-Host "`n=== SCHOOL ID 3 DETAILS ===" -ForegroundColor Cyan
    while ($reader.Read()) {
        Write-Host "SchoolId: $($reader['SchoolId'])"
        Write-Host "DistrictId: $($reader['DistrictId'])"
        Write-Host "CleverSchoolId: $($reader['CleverSchoolId'])"
        Write-Host "Name: $($reader['Name'])"
        Write-Host "KeyVaultSchoolPrefix: $($reader['KeyVaultSchoolPrefix'])"
        Write-Host "DatabaseName: $($reader['DatabaseName'])"
        Write-Host "IsActive: $($reader['IsActive'])"
        Write-Host "RequiresFullSync: $($reader['RequiresFullSync'])"
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
