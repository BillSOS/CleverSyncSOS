-- =============================================
-- Migration: ChangeDistrictIdToString
-- Description: Safely change Schools.DistrictId from INT to NVARCHAR(50)
-- =============================================

BEGIN TRANSACTION;

BEGIN TRY
    PRINT 'Starting migration: ChangeDistrictIdToString';
    PRINT '';

    DECLARE @SchoolCount INT;
    DECLARE @IndexName NVARCHAR(128);
    DECLARE @ConstraintName NVARCHAR(128);

    -- =============================================
    -- Step 1: Drop existing foreign key
    -- =============================================
    PRINT 'Step 1: Dropping foreign key constraint if exists...';

    IF EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = 'FK_Schools_Districts_DistrictId'
          AND parent_object_id = OBJECT_ID('Schools')
    )
    BEGIN
        ALTER TABLE Schools DROP CONSTRAINT FK_Schools_Districts_DistrictId;
        PRINT '  ✓ Dropped FK_Schools_Districts_DistrictId';
    END
    ELSE
    BEGIN
        PRINT '  ⚠ Foreign key not found — skipping';
    END
    PRINT '';

    -- =============================================
    -- Step 2: Check for data and add temp column
    -- =============================================
    PRINT 'Step 2: Checking for existing school records...';
    SELECT @SchoolCount = COUNT(*) FROM Schools;

    IF @SchoolCount > 0
    BEGIN
        PRINT '  Found ' + CAST(@SchoolCount AS VARCHAR(10)) + ' schools. Adding temp column...';
        IF COL_LENGTH('Schools', 'DistrictId_New') IS NULL
            EXEC('ALTER TABLE Schools ADD DistrictId_New NVARCHAR(50) NULL;');

        EXEC('UPDATE s
              SET s.DistrictId_New = d.CleverDistrictId
              FROM Schools s
              INNER JOIN Districts d ON s.DistrictId = d.DistrictId;');

        PRINT '  ✓ Migrated DistrictId values to new column';
    END
    ELSE
    BEGIN
        PRINT '  No schools found — skipping data migration.';
    END
    PRINT '';

    -- =============================================
    -- Step 3: Drop dependent objects and alter column
    -- =============================================
    PRINT 'Step 3: Altering DistrictId column type safely...';

    -- Drop indexes referencing DistrictId
    SELECT TOP 1 @IndexName = i.name
    FROM sys.indexes i
    INNER JOIN sys.index_columns ic
        ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.object_id = OBJECT_ID('Schools')
      AND COL_NAME(ic.object_id, ic.column_id) = 'DistrictId';

    IF @IndexName IS NOT NULL
    BEGIN
        PRINT '  ⚠ Dropping index: ' + @IndexName;
        EXEC('DROP INDEX [' + @IndexName + '] ON Schools;');
    END

    -- Drop default constraint if it exists
    SELECT @ConstraintName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    WHERE c.object_id = OBJECT_ID('Schools')
      AND c.name = 'DistrictId';

    IF @ConstraintName IS NOT NULL
    BEGIN
        PRINT '  ⚠ Dropping default constraint: ' + @ConstraintName;
        EXEC('ALTER TABLE Schools DROP CONSTRAINT [' + @ConstraintName + ']');
    END

    -- Now change or replace column
    IF @SchoolCount > 0
    BEGIN
        IF COL_LENGTH('Schools', 'DistrictId') IS NOT NULL
            ALTER TABLE Schools DROP COLUMN DistrictId;

        EXEC sp_rename 'Schools.DistrictId_New', 'DistrictId', 'COLUMN';
        PRINT '  ✓ Replaced DistrictId column';
    END
    ELSE
    BEGIN
        ALTER TABLE Schools ALTER COLUMN DistrictId NVARCHAR(50) NOT NULL;
        PRINT '  ✓ Altered DistrictId column type';
    END
    PRINT '';

    -- =============================================
    -- Step 4: Ensure unique constraint on Districts
    -- =============================================
    PRINT 'Step 4: Ensuring unique constraint on Districts.CleverDistrictId...';

    IF NOT EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE name = 'AK_Districts_CleverDistrictId'
          AND parent_object_id = OBJECT_ID('Districts')
    )
    BEGIN
        ALTER TABLE Districts
        ADD CONSTRAINT AK_Districts_CleverDistrictId UNIQUE (CleverDistrictId);
        PRINT '  ✓ Added unique constraint';
    END
    ELSE
    BEGIN
        PRINT '  ⚠ Unique constraint already exists';
    END
    PRINT '';

    -- =============================================
    -- Step 5: Recreate foreign key
    -- =============================================
    PRINT 'Step 5: Recreating foreign key constraint...';

    ALTER TABLE Schools
    ADD CONSTRAINT FK_Schools_Districts_DistrictId
    FOREIGN KEY (DistrictId)
    REFERENCES Districts(CleverDistrictId)
    ON DELETE NO ACTION;

    PRINT '  ✓ Recreated foreign key constraint';
    PRINT '';

    COMMIT TRANSACTION;
    PRINT '✓✓✓ Migration completed successfully ✓✓✓';

END TRY
BEGIN CATCH
    PRINT '✗ ERROR OCCURRED ✗';
    PRINT 'Error Message: ' + ERROR_MESSAGE();
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    RAISERROR('Migration failed.', 16, 1);
END CATCH;
GO
