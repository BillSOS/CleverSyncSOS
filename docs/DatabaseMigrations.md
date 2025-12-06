# Database Migrations Guide

**Version:** 2.0.0
**Last Updated:** 2025-11-25

## Overview

CleverSyncSOS uses a dual-database architecture with Entity Framework Core 9.0 for database management:

1. **SessionDb** - Orchestration database containing Districts, Schools, SyncHistory, Users, and AuditLogs
2. **Per-School Databases** - Individual databases for each school containing Students and Teachers

## Migration Files

### SessionDb Migrations

Located in: `src/CleverSyncSOS.Core/Database/SessionDb/Migrations/`

1. **20251111181216_InitialCreate_SessionDb.cs**
   - Creates Districts table with CleverDistrictId unique index
   - Creates Schools table with CleverSchoolId unique index and DistrictId foreign key
   - Creates SyncHistory table with composite index on (SchoolId, EntityType, SyncEndTime)

2. **20251111182150_AddSyncEnhancements_SessionDb.cs**
   - Adds SyncType (enum) to SyncHistory with default value 2 (Incremental)
   - Adds RequiresFullSync (bool) to Schools with default value false

3. **20251121011048_AddDistrictAndSchoolPrefixes.cs**
   - Adds DistrictPrefix (nvarchar(100)) to Districts table
   - Adds SchoolPrefix (nvarchar(100)) to Schools table
   - Adds indexes on these fields for Key Vault secret naming
   - **Purpose**: Standardizes Key Vault secret naming pattern

4. **20251119192327_AddUsersAndAuditLog_SessionDb.cs**
   - Creates Users table for Admin Portal authentication
   - Creates AuditLogs table for security logging
   - Adds role-based access control fields
   - **Purpose**: Enables Admin Portal functionality

5. **AddLocalTimeZoneToDistricts** (Recent migration)
   - Adds LocalTimeZone (nvarchar(100)) field to Districts table
   - Defaults to "Eastern Standard Time"
   - **Purpose**: Enables timezone-aware timestamp display in Admin Portal
   - **Impact**: All timestamps in the Admin Portal now display in the district's local timezone

### SchoolDb Migrations

Located in: `src/CleverSyncSOS.Core/Database/SchoolDb/Migrations/`

1. **20251111181231_InitialCreate_SchoolDb.cs**
   - Creates Students table with CleverStudentId unique index
   - Creates Teachers table with CleverTeacherId unique index

2. **20251111182207_AddSyncEnhancements_SchoolDb.cs**
   - Adds IsActive (bool) and DeactivatedAt (DateTime?) to Students
   - Adds IsActive (bool) and DeactivatedAt (DateTime?) to Teachers
   - Adds indexes on IsActive for both tables (for efficient queries during full sync)

## Configuration

### SessionDb Connection String

Configure the SessionDb connection string in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "SessionDb": "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;Persist Security Info=False;User ID=SOSAdmin;Password={password};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
  }
}
```

**Security Note**: The password should be stored in Azure Key Vault and retrieved via configuration, not hardcoded.

**Recommended**: Use the standardized secret naming pattern:
- Key Vault Name: `cleversync-kv`
- Secret Name: `CleverSyncSOS--SessionDb--ConnectionString`

### Per-School Connection Strings

Per-school connection strings are stored in Azure Key Vault using the school's `SchoolPrefix` field.

**Naming Pattern**: `CleverSyncSOS--{SchoolPrefix}--ConnectionString`

**Examples**:
- `CleverSyncSOS--Lincoln-Elementary--ConnectionString`
- `CleverSyncSOS--Washington-Middle--ConnectionString`
- `CleverSyncSOS--Jefferson-High--ConnectionString`

See [Naming Conventions](Naming-Conventions.md) for complete details.

## Applying Migrations

### Apply SessionDb Migration

```bash
# Navigate to Core project
cd src/CleverSyncSOS.Core

# Apply SessionDb migrations
dotnet ef database update --context SessionDbContext --connection "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SOSAdmin;Password={password};Encrypt=True;"
```

### Apply SchoolDb Migration to a School Database

Since each school has its own database, you'll need to apply the SchoolDb migration to each school's database individually:

```bash
# Navigate to Core project
cd src/CleverSyncSOS.Core

# Apply SchoolDb migrations to a specific school database
dotnet ef database update --context SchoolDbContext --connection "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=School_LincolnHigh;User ID=SOSAdmin;Password={password};Encrypt=True;"
```

**Note**: In production, the SchoolDb migrations will be applied automatically by the `SchoolDatabaseConnectionFactory` when a new school is added to the system.

## Creating New Migrations

### SessionDb Migration

```bash
cd src/CleverSyncSOS.Core
dotnet ef migrations add <MigrationName> --context SessionDbContext --output-dir Database/SessionDb/Migrations
```

### SchoolDb Migration

```bash
cd src/CleverSyncSOS.Core
dotnet ef migrations add <MigrationName> --context SchoolDbContext --output-dir Database/SchoolDb/Migrations
```

## Rollback Migrations

### Remove Last Migration (Before Applying)

```bash
# SessionDb
dotnet ef migrations remove --context SessionDbContext

# SchoolDb
dotnet ef migrations remove --context SchoolDbContext
```

### Rollback Applied Migration

```bash
# SessionDb - rollback to specific migration
dotnet ef database update <PreviousMigrationName> --context SessionDbContext

# SchoolDb - rollback to specific migration
dotnet ef database update <PreviousMigrationName> --context SchoolDbContext
```

## Migration Strategy

### Initial Setup (New Environment)

1. **Create SessionDb database** in Azure SQL Database
2. **Apply SessionDb migrations** using the command above
3. **Populate Districts and Schools** tables with your district/school data
4. **Create per-school databases** in Azure SQL Database (one per school)
5. **Store connection strings** in Azure Key Vault with references in Schools table
6. **Apply SchoolDb migrations** to each school database

### Adding a New School

1. **Create new school database** in Azure SQL Database
2. **Apply SchoolDb migrations** to the new school database
3. **Store connection string** in Azure Key Vault
4. **Insert School record** in SessionDb.Schools with Key Vault secret name
5. **Run initial full sync** for the school using `ISyncService.SyncSchoolAsync(schoolId, forceFullSync: true)`

### Updating Schema

When adding new fields or tables:

1. **Update entity classes** in `Database/SessionDb/Entities` or `Database/SchoolDb/Entities`
2. **Update DbContext** configuration in `OnModelCreating` if needed
3. **Create migration** using the commands above
4. **Review migration code** to ensure correctness
5. **Test migration** in a development environment first
6. **Apply migration** to production databases

## Troubleshooting

### Error: "No DbContext was found"

Make sure you're running the command from the `src/CleverSyncSOS.Core` directory where the DbContext classes are defined.

### Error: "Build failed"

Ensure the project builds successfully before creating migrations:
```bash
dotnet build
```

### EF Core Tools Version Warning

If you see a warning about EF Core tools version mismatch, update the global tools:
```bash
dotnet tool update --global dotnet-ef
```

### Connection String Issues

- Ensure the connection string has proper escaping for special characters
- Verify the database server allows connections from your IP
- Check Azure SQL firewall rules
- Verify credentials are correct

### LocalDB vs Azure SQL

**Problem**: Migrations apply to LocalDB instead of Azure SQL

**Cause**: The Console or startup project's `appsettings.json` has a LocalDB connection string, and EF Core defaults to using it.

**Solutions**:

**Option 1: Explicit Connection String (Recommended)**
```bash
# Always specify connection string explicitly
dotnet ef database update --context SessionDbContext \
  --connection "Server=tcp:your-server.database.windows.net,1433;Initial Catalog=SessionDb;User ID=SOSAdmin;Password=YOUR_PASSWORD;Encrypt=True;"
```

**Option 2: Use Azure Data Studio (Manual)**
If migrations fail, create a SQL script and execute manually:
```bash
# Generate SQL script
dotnet ef migrations script --context SessionDbContext --output migration.sql

# Execute in Azure Data Studio connected to Azure SQL
```

**Option 3: Verify Connection Before Migrating**
```bash
# Use verbose flag to see which database EF Core connects to
dotnet ef database update --context SessionDbContext --verbose
```

### Azure SQL Firewall Blocks Connection

**Symptoms**: Timeout or "Cannot open server" errors

**Solutions**:
1. Add your IP to SQL Server firewall:
   ```bash
   az sql server firewall-rule create \
     --resource-group your-rg \
     --server your-server \
     --name YourIP \
     --start-ip-address YOUR_IP \
     --end-ip-address YOUR_IP
   ```

2. Or use Azure Portal Query Editor (already has access)

### Multiple DbContexts Confusion

**Problem**: EF Core applies migration to wrong context

**Solution**: Always specify `--context` parameter:
```bash
# SessionDb migrations
dotnet ef database update --context SessionDbContext

# SchoolDb migrations
dotnet ef database update --context SchoolDbContext
```

## Best Practices

1. **Always review migration code** before applying to production
2. **Backup databases** before applying migrations
3. **Test migrations** in a development/staging environment first
4. **Use transactions** - EF Core migrations are wrapped in transactions by default
5. **Document breaking changes** in migration comments
6. **Keep migrations small** - create focused migrations for related changes
7. **Never modify applied migrations** - create new migrations instead
8. **Version control** - commit migrations to source control

## Schema Diagram

### SessionDb Schema

```
Districts
├── DistrictId (PK)
├── CleverDistrictId (Unique)
├── Name
├── DistrictPrefix (Indexed) - For Key Vault secret naming
├── LocalTimeZone (nvarchar(100), default: "Eastern Standard Time")
├── CreatedAt
└── UpdatedAt

Schools
├── SchoolId (PK)
├── DistrictId (nvarchar(50), FK -> Districts.CleverDistrictId)
├── CleverSchoolId (Unique)
├── Name
├── DatabaseName
├── SchoolPrefix (Indexed) - For Key Vault secret naming
├── IsActive
├── RequiresFullSync
├── CreatedAt
└── UpdatedAt

SyncHistory
├── SyncId (PK)
├── SchoolId (FK -> Schools)
├── EntityType
├── SyncType (enum: Full=1, Incremental=2, Reconciliation=3)
├── SyncStartTime
├── SyncEndTime
├── Status
├── RecordsProcessed
├── RecordsFailed
├── ErrorMessage
└── LastSyncTimestamp

Users (Admin Portal)
├── UserId (PK)
├── Email (Unique)
├── CleverUserId
├── Role (SuperAdmin, DistrictAdmin, SchoolAdmin)
├── DistrictId (FK -> Districts.CleverDistrictId)
├── SchoolId (FK -> Schools.SchoolId)
├── IsActive
├── LastLoginAt
├── CreatedAt
└── UpdatedAt

AuditLogs (Admin Portal)
├── AuditLogId (PK)
├── UserId (FK -> Users)
├── Action (Indexed)
├── Details
├── IpAddress
├── UserAgent
├── Success
├── ErrorMessage
└── CreatedAt (Indexed)
```

### SchoolDb Schema (Per-School)

```
Students
├── StudentId (PK)
├── CleverStudentId (Unique)
├── FirstName
├── LastName
├── Email
├── Grade
├── StudentNumber
├── LastModifiedInClever
├── IsActive (Indexed)
├── DeactivatedAt
├── CreatedAt
└── UpdatedAt

Teachers
├── TeacherId (PK)
├── CleverTeacherId (Unique)
├── FirstName
├── LastName
├── Email
├── Title
├── LastModifiedInClever
├── IsActive (Indexed)
├── DeactivatedAt
├── CreatedAt
└── UpdatedAt
```

## Related Documentation

- **[Naming Conventions](Naming-Conventions.md)** - Key Vault secret naming standards
- **[Configuration Setup](ConfigurationSetup.md)** - Database and Key Vault configuration
- **[Quick Start Guide](QuickStart.md)** - Complete deployment guide
- **[Admin Portal Quick Start](AdminPortal-QuickStart.md)** - Admin Portal deployment
- **[Security Architecture](SecurityArchitecture.md)** - Security best practices
- [SpecKit Data Model](../SpecKit/DataModel/001-clever-api-auth/DataModel.md)
- [Implementation Plan](../SpecKit/Plans/001-clever-api-auth/plan.md)
- [Feature Specification](../SpecKit/Specs/001-clever-api-auth/spec-1.md)

---

**Version**: 2.0.0
**Last Updated**: 2025-11-25
