-- Add Terms table to SchoolDb (per-school databases)
-- Run this in Azure Portal Query Editor or SQL Server Management Studio
-- against each SchoolDb instance (e.g., CityHighSchoolDb)
--
-- Terms are district-level in Clever API but stored per-school for data isolation.
-- This supports Clever Secure Sync Certification requirements for term synchronization.

-- NOTE: Run this script against the SchoolDb, NOT SessionDb
-- Example: USE CityHighSchoolDb;

-- Check if table already exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Terms')
BEGIN
    -- Create the Terms table
    CREATE TABLE [dbo].[Terms] (
        [TermId] int IDENTITY(1,1) NOT NULL,
        [CleverTermId] nvarchar(50) NOT NULL,
        [CleverDistrictId] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NULL,
        [StartDate] datetime2 NULL,
        [EndDate] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [DeletedAt] datetime2 NULL,
        [LastEventReceivedAt] datetime2 NULL,
        [LastSyncedAt] datetime2 NULL,
        CONSTRAINT [PK_Terms] PRIMARY KEY ([TermId])
    );

    -- Create unique index on CleverTermId (external identifier)
    CREATE UNIQUE INDEX [IX_Terms_CleverTermId] ON [dbo].[Terms] ([CleverTermId]);

    -- Create index on CleverDistrictId for filtering by district
    CREATE INDEX [IX_Terms_CleverDistrictId] ON [dbo].[Terms] ([CleverDistrictId]);

    -- Create index on DeletedAt for soft-delete filtering (NULL = active)
    CREATE INDEX [IX_Terms_DeletedAt] ON [dbo].[Terms] ([DeletedAt]);

    -- Create index on LastSyncedAt for orphan detection during full sync
    CREATE INDEX [IX_Terms_LastSyncedAt] ON [dbo].[Terms] ([LastSyncedAt]);

    PRINT 'Terms table created successfully';
END
ELSE
BEGIN
    PRINT 'Terms table already exists';

    -- Add any missing columns if upgrading from an earlier version
    -- (none expected for initial implementation)
END
GO
