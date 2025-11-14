-- =============================================
-- Rollback: ChangeDistrictIdToString
-- Description: Reverts Schools.DistrictId from nvarchar(50) back to int
-- Date: 2025-01-12
-- WARNING: This will cause data loss if you have schools
--          that don't match to a Districts.DistrictId integer
-- =============================================

USE SessionDb;
GO

-- Start transaction for safety
BEGIN TRANSACTION;

BEGIN TRY
    PRINT 'Starting rollback: ChangeDistrictIdToString';
    PRINT '';
    PRINT '⚠⚠⚠ WARNING ⚠⚠⚠';
    PRINT 'This rollback may cause data loss if Schools.DistrictId contains';
    PRINT 'values that cannot be mapped back to Districts.DistrictId (int)';
    PRINT '';

    -- =============================================
    -- Step 1: Drop existing foreign key constraint
    -- =============================================
    PRINT 'Step 1: Dropping foreign key constraint FK_Schools_Districts_DistrictId';

    IF EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = 'FK_Schools_Districts_DistrictId'
          AND parent_object_id = OBJECT_ID('Schools')
    )
    BEGIN
        ALTER TABLE Schools
        DROP CONSTRAINT FK_Schools_Districts_DistrictId;
        PRINT '  ✓ Foreign key constraint dropped';
    END
    ELSE
    BEGIN
        PRINT '  ⚠ Foreign key constraint does not exist (skipping)';
    END
    PRINT '';

    -- =============================================
    -- Step 2: Drop unique constraint on Districts.CleverDistrictId
    -- =============================================
    PRINT 'Step 2: Dropping unique constraint on Districts.CleverDistrictId';

    IF EXISTS (
        SELECT 1
        FROM sys.key_constraints
        WHERE name = 'AK_Districts_CleverDistrictId'
          AND parent_object_id = OBJECT_ID('Districts')
    )
    BEGIN
        ALTER TABLE Districts
        DROP CONSTRAINT AK_Districts_CleverDistrictId;
        PRINT '  ✓ Unique constraint dropped';
    END
    ELSE
    BEGIN
        PRINT '  ⚠ Unique constraint does not exist (skipping)';
    END
    PRINT '';

    -- =============================================
    -- Step 3: Migrate existing data back to int (if any)
    -- =============================================
    PRINT 'Step 3: Checking for existing data in Schools table';

    DECLARE @SchoolCount INT;
    SELECT @SchoolCount = COUNT(*) FROM Schools;

    IF @SchoolCount > 0
    BEGIN
        PRINT '  Found ' + CAST(@SchoolCount AS VARCHAR(10)) + ' school records';
        PRINT '  Creating temporary column for data migration';

        -- Add temporary column to store integer DistrictId values
        ALTER TABLE Schools ADD DistrictId_New INT NULL;

        -- Copy DistrictId values back to integer
        UPDATE s
        SET s.DistrictId_New = d.DistrictId
        FROM Schools s
        INNER JOIN Districts d ON s.DistrictId = d.CleverDistrictId;

        -- Check for unmapped records
        DECLARE @UnmappedCount INT;
        SELECT @UnmappedCount = COUNT(*)
        FROM Schools
        WHERE DistrictId_New IS NULL;

        IF @UnmappedCount > 0
        BEGIN
            PRINT '  ✗ ERROR: Found ' + CAST(@UnmappedCount AS VARCHAR(10)) + ' schools that cannot be mapped back to integer DistrictId';
            THROW 50003, 'Cannot rollback: Schools exist that do not map to Districts.DistrictId', 1;
        END

        PRINT '  ✓ Data migrated to temporary column';
    END
    ELSE
    BEGIN
        PRINT '  No existing data to migrate';
    END
    PRINT '';

    -- =============================================
    -- Step 4: Drop old DistrictId column and rename new one
    -- =============================================
    PRINT 'Step 4: Reverting DistrictId column type to int';

    IF @SchoolCount > 0
    BEGIN
        -- Drop old column
        ALTER TABLE Schools DROP COLUMN DistrictId;

        -- Rename new column
        EXEC sp_rename 'Schools.DistrictId_New', 'DistrictId', 'COLUMN';

        -- Make it NOT NULL
        ALTER TABLE Schools ALTER COLUMN DistrictId INT NOT NULL;

        PRINT '  ✓ Column reverted to int';
    END
    ELSE
    BEGIN
        -- No data, so we can directly alter the column
        ALTER TABLE Schools ALTER COLUMN DistrictId INT NOT NULL;
        PRINT '  ✓ Column type changed to int';
    END
    PRINT '';

    -- =============================================
    -- Step 5: Recreate foreign key constraint
    -- =============================================
    PRINT 'Step 5: Recreating foreign key constraint';

    ALTER TABLE Schools
    ADD CONSTRAINT FK_Schools_Districts_DistrictId
    FOREIGN KEY (DistrictId)
    REFERENCES Districts(DistrictId)
    ON DELETE NO ACTION;

    PRINT '  ✓ Foreign key constraint created';
    PRINT '';

    PRINT '✓✓✓ Rollback completed successfully! ✓✓✓';
    PRINT '';

    -- Commit transaction
    COMMIT TRANSACTION;
    PRINT 'Transaction committed.';

END TRY
BEGIN CATCH
    -- Rollback on error
    PRINT '';
    PRINT '✗✗✗ ERROR OCCURRED ✗✗✗';
    PRINT 'Error Message: ' + ERROR_MESSAGE();
    PRINT 'Error Number: ' + CAST(ERROR_NUMBER() AS VARCHAR(10));
    PRINT 'Error Line: ' + CAST(ERROR_LINE() AS VARCHAR(10));
    PRINT '';

    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT 'Transaction rolled back.';
    END

    THROW;
END CATCH
GO
