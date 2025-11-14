# Database Migrations Guide

## Overview

CleverSyncSOS uses a dual-database architecture with Entity Framework Core 9.0 for database management:

1. **SessionDb** - Orchestration database containing Districts, Schools, and SyncHistory
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

### Per-School Connection Strings

Per-school connection strings are stored in Azure Key Vault and referenced in the `Schools.KeyVaultConnectionStringSecretName` column.

Example Key Vault secret name: `School-LincolnHigh-ConnectionString`

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
├── KeyVaultSecretPrefix
├── CreatedAt
└── UpdatedAt

Schools
├── SchoolId (PK)
├── DistrictId (nvarchar(50), FK -> Districts.CleverDistrictId)
├── CleverSchoolId (Unique)
├── Name
├── DatabaseName
├── KeyVaultConnectionStringSecretName
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

- [SpecKit Data Model](../SpecKit/DataModel/001-clever-api-auth/DataModel.md)
- [Implementation Plan](../SpecKit/Plans/001-clever-api-auth/plan.md)
- [Feature Specification](../SpecKit/Specs/001-clever-api-auth/spec-1.md)
