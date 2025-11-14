-- Verification script for CleverSyncSOS database sync
-- Run this against your school database to verify sync results

-- Check student count
SELECT 'Total Students' AS Metric, COUNT(*) AS Count FROM Students;

-- Check teacher count
SELECT 'Total Teachers' AS Metric, COUNT(*) AS Count FROM Teachers;

-- Check active vs inactive students
SELECT
    CASE WHEN IsActive = 1 THEN 'Active Students' ELSE 'Inactive Students' END AS Status,
    COUNT(*) AS Count
FROM Students
GROUP BY IsActive;

-- Check active vs inactive teachers
SELECT
    CASE WHEN IsActive = 1 THEN 'Active Teachers' ELSE 'Inactive Teachers' END AS Status,
    COUNT(*) AS Count
FROM Teachers
GROUP BY IsActive;

-- Sample students (first 5)
SELECT TOP 5
    CleverStudentId,
    FirstName,
    MiddleName,
    LastName,
    StudentNumber,
    SisId,
    IsActive
FROM Students
ORDER BY LastName, FirstName;

-- Sample teachers (first 5)
SELECT TOP 5
    CleverTeacherId,
    FirstName,
    LastName,
    Email,
    Title,
    IsActive
FROM Teachers
ORDER BY LastName, FirstName;
