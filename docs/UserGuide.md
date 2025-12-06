# CleverSyncSOS User Guide

**Version:** 2.0.0
**Last Updated:** 2025-11-25

---

## 🎯 Choose Your Interface

### Recommended: Admin Portal (GUI)

For most users, we recommend using the **CleverSync Admin Portal** - a web-based interface that makes managing CleverSyncSOS easy without any command-line experience.

**Production URL:** https://cleversyncsos.azurewebsites.net

**Admin Portal provides:**
- ✅ **Web-based dashboard** - Visual overview of all sync operations
- ✅ **Point-and-click management** - No CLI or SQL knowledge required
- ✅ **Real-time monitoring** - See sync status and history instantly
- ✅ **Role-based access** - School admins see their school, district admins see their district
- ✅ **Configuration management** - Update settings through a GUI
- ✅ **Audit logging** - Track all administrative actions

📖 **See:** [Admin Portal User Guide](AdminPortal-User-Guide.md)

### Alternative: Command-Line Tools (Advanced)

This guide covers command-line and API-based operations for:
- System administrators
- Automation and scripting
- Advanced troubleshooting
- CI/CD integration

**If you prefer a graphical interface, use the Admin Portal instead!**

---

## Table of Contents

1. [What is CleverSyncSOS?](#what-is-cleversyncsos)
2. [How It Works](#how-it-works)
3. [Daily Operations](#daily-operations)
4. [Triggering a Manual Sync](#triggering-a-manual-sync)
5. [Monitoring Sync Status](#monitoring-sync-status)
6. [Understanding Health Checks](#understanding-health-checks)
7. [Troubleshooting Common Issues](#troubleshooting-common-issues)
8. [FAQ](#faq)
9. [Getting Help](#getting-help)

---

## What is CleverSyncSOS?

CleverSyncSOS automatically keeps your school's student and teacher information up-to-date by syncing data from Clever (your district's Student Information System) to your school's database.

### What It Does

- **Automatically syncs** student and teacher data from Clever every day at 2 AM
- **Updates your database** with the latest information from your SIS
- **Removes students** who are no longer enrolled
- **Adds new students** who join your school
- **Updates changes** like contact information, grade levels, and teacher assignments

### What You Get

- Always up-to-date student and teacher information
- No manual data entry required
- Automatic daily updates
- On-demand sync when you need it
- Health monitoring to ensure everything is working

---

## How It Works

### The Daily Sync Process

Every day at **2:00 AM UTC** (adjust for your timezone), CleverSyncSOS:

1. **Connects to Clever** using secure OAuth authentication
2. **Retrieves data** for your school(s)
3. **Compares** the Clever data with your current database
4. **Updates** any changed information
5. **Adds** new students and teachers
6. **Deactivates** students who are no longer enrolled
7. **Logs** the results for monitoring

This entire process is **automatic** and requires no user intervention.

### Types of Syncs

**Full Sync** (Default for first sync)
- Downloads all students and teachers
- Removes students not in Clever
- Takes longer but ensures complete accuracy
- Used when: First sync, after long downtime, or when you suspect data issues

**Incremental Sync** (Daily default after first sync)
- Only downloads changed records
- Much faster (typically under 1 minute)
- Used when: Regular daily operations

---

## Daily Operations

### What You Should Do Daily

**Morning Check** (recommended at start of business day):

1. **Check your email** - You should receive automated reports if syncs fail
2. **Verify the health status** - Visit the health check page (see below)
3. **Spot-check data** - Randomly verify a few student records in your database

### What the System Does Automatically

- Runs sync at 2 AM daily
- Retries if Clever API is temporarily down
- Logs all operations
- Sends alerts if something fails
- Maintains connection to Azure Key Vault for credentials

---

## Triggering a Manual Sync

Sometimes you need to sync immediately rather than waiting for the 2 AM automatic sync.

### When to Use Manual Sync

- New student enrolled today and you need their data now
- Teacher information changed and you need it immediately
- After fixing a configuration issue
- After adding a new school to the system
- Testing after maintenance

### How to Trigger a Manual Sync

#### Option 1: Using the Web Interface (if deployed)

```
POST to: https://your-function-app.azurewebsites.net/api/sync
Headers: x-functions-key: YOUR_FUNCTION_KEY
```

Use a tool like Postman, or ask your IT administrator.

#### Option 2: Using Azure Portal

1. Log into **Azure Portal**
2. Navigate to **Function Apps**
3. Select **CleverSyncSOS-Functions**
4. Click **Functions** → **ManualSync**
5. Click **Test/Run**
6. Add query parameters if needed (see below)
7. Click **Run**

### Manual Sync Options

**Sync a specific school:**
```
?schoolId=3
```

**Sync all schools in a district:**
```
?districtId=1
```

**Force a full sync (not incremental):**
```
?schoolId=3&forceFullSync=true
```

**Sync everything:**
```
(no parameters)
```

### What Happens During Manual Sync

1. You trigger the sync
2. System connects to Clever API
3. Downloads the data
4. Updates your database
5. Returns a JSON response with results

**Example Response:**
```json
{
  "success": true,
  "scope": "school",
  "schoolId": 3,
  "schoolName": "City High School",
  "syncType": "Full",
  "duration": 58.92,
  "statistics": {
    "studentsProcessed": 1488,
    "studentsFailed": 0,
    "studentsDeleted": 0,
    "teachersProcessed": 82,
    "teachersFailed": 0,
    "teachersDeleted": 0
  },
  "timestamp": "2025-11-13T23:30:00.000Z"
}
```

---

## Monitoring Sync Status

### Where to Check Sync Status

#### 1. Sync History Database

Connect to your **SessionDb** database and run:

```sql
-- View recent syncs
SELECT TOP 10
    SchoolId,
    SchoolName,
    SyncType,
    StartedAt,
    CompletedAt,
    DATEDIFF(SECOND, StartedAt, CompletedAt) AS DurationSeconds,
    TotalRecordsProcessed,
    RecordsFailed,
    Success,
    ErrorMessage
FROM SyncHistory
ORDER BY StartedAt DESC;
```

#### 2. Application Insights (if configured)

1. Log into **Azure Portal**
2. Navigate to **Application Insights** → **CleverSyncSOS**
3. View **Metrics** and **Logs**
4. Check for errors or warnings

#### 3. Health Check Endpoints

Visit these URLs to check system health:

- `https://your-api.azurewebsites.net/health` - Overall health
- `https://your-api.azurewebsites.net/health/clever-auth` - Clever authentication status
- `https://your-api.azurewebsites.net/health/ready` - System readiness

### Understanding Sync Results

**Success Indicators:**
- `Success = true` in SyncHistory
- `RecordsFailed = 0`
- CompletedAt timestamp is recent
- No error messages

**Warning Signs:**
- `RecordsFailed > 0` (some records failed but sync continued)
- Very long duration (may indicate API slowness)
- Frequent retries in logs

**Critical Issues:**
- `Success = false`
- ErrorMessage contains authentication errors
- No recent syncs in SyncHistory

---

## Understanding Health Checks

Health checks tell you if the system is working properly.

### Health Check Endpoints

#### `/health` - Overall System Health

**What it checks:**
- Clever authentication is working
- System is ready to sync
- No critical errors

**Status Codes:**
- **Healthy** (green) - Everything working
- **Degraded** (yellow) - Some issues but operational
- **Unhealthy** (red) - Critical problems

**Example Healthy Response:**
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0862",
  "entries": {
    "clever_authentication": {
      "status": "Healthy",
      "description": "Clever authentication is healthy",
      "duration": "00:00:00.0862"
    }
  }
}
```

#### `/health/clever-auth` - Authentication Status

**What it checks:**
- Connection to Azure Key Vault
- Valid Clever API credentials
- Ability to obtain access tokens
- Token expiration status

**What to look for:**
- Duration should be < 100ms (cached checks are very fast)
- Status should be "Healthy"
- No error descriptions

#### `/health/live` - Liveness Probe

**What it checks:**
- Application is running
- Not crashed or frozen

**Use this for:**
- Kubernetes liveness probes
- Load balancer health checks
- Uptime monitoring

#### `/health/ready` - Readiness Probe

**What it checks:**
- Application is ready to handle requests
- Dependencies are available
- Clever authentication is working

**Use this for:**
- Kubernetes readiness probes
- Determining if system can process syncs

### What to Do Based on Health Status

**Healthy**
- No action needed
- System operating normally

**Degraded**
- Check Application Insights logs
- Review recent sync history
- May still be operational but investigate

**Unhealthy**
- **Immediate action required**
- Check error messages in health response
- Verify Azure Key Vault access
- Check Clever API credentials
- Contact IT support

---

## Troubleshooting Common Issues

### Issue: No Recent Syncs

**Symptoms:**
- SyncHistory shows no syncs in past 24 hours
- Health check shows unhealthy status

**Possible Causes:**
1. Timer function is not running
2. Azure Function App is stopped
3. Clever API credentials expired

**Solutions:**
1. Check Azure Function App status in Azure Portal
2. Verify Timer Function is enabled
3. Check Application Insights for error logs
4. Verify Clever API credentials in Key Vault
5. Trigger a manual sync to test

### Issue: Partial Sync Failures

**Symptoms:**
- `RecordsFailed > 0` in SyncHistory
- Some students synced, others didn't

**Possible Causes:**
1. Invalid data from Clever API
2. Database constraints (duplicate IDs, etc.)
3. Network interruption during sync

**Solutions:**
1. Review ErrorMessage in SyncHistory
2. Check Application Insights for specific errors
3. Trigger another sync - may resolve transient issues
4. If persistent, contact support with error details

### Issue: Authentication Failures

**Symptoms:**
- Health check shows "Unhealthy" for clever_authentication
- Sync fails with 401 Unauthorized error
- Logs show "Failed to retrieve credentials"

**Possible Causes:**
1. Azure Key Vault credentials missing or wrong
2. Managed identity not configured
3. Clever API credentials expired or revoked

**Solutions:**
1. Verify secrets exist in Key Vault (`cleversync-kv`):
   - `CleverSyncSOS--Clever--ClientId`
   - `CleverSyncSOS--Clever--ClientSecret`
   - `CleverSyncSOS--Clever--AccessToken` (if using district token)
2. Check managed identity has Key Vault access
3. Re-create Clever OAuth credentials if needed
4. Check Clever Developer Dashboard for API status
5. **Admin Portal users**: Use the Configuration page to view/update secrets

### Issue: Slow Syncs

**Symptoms:**
- Sync takes longer than expected
- Duration > 5 minutes for < 2000 students

**Possible Causes:**
1. Clever API rate limiting
2. Network latency
3. Database performance issues
4. Full sync when incremental would work

**Solutions:**
1. Check logs for rate limit warnings (HTTP 429)
2. System automatically retries with backoff
3. Consider scheduling syncs during off-peak hours
4. Verify incremental sync is being used (not forcing full sync)

### Issue: Students Not Deactivated

**Symptoms:**
- Students who left school still showing as active
- Database has more students than Clever

**Possible Causes:**
1. Using incremental sync (doesn't remove students)
2. Hard-delete feature not enabled

**Solutions:**
1. Trigger a **full sync** with `?forceFullSync=true`
2. Full sync compares all records and deactivates missing students
3. Verify `RequiresFullSync` flag in Schools table

### Issue: Database Connection Errors

**Symptoms:**
- Sync fails with "Cannot connect to database"
- Logs show SQL connection timeout

**Possible Causes:**
1. Database server firewall blocking Azure Functions
2. Connection string incorrect in Key Vault
3. Database server down or overloaded

**Solutions:**
1. Add Azure Functions IP to SQL Server firewall rules
2. Verify connection strings in Key Vault (`cleversync-kv`):
   - `CleverSyncSOS--SessionDb--ConnectionString`
   - `CleverSyncSOS--{SchoolPrefix}--ConnectionString` (e.g., `CleverSyncSOS--Lincoln-Elementary--ConnectionString`)
3. Test connection from Azure Portal Query Editor
4. Check Azure SQL Database status
5. **Admin Portal users**: Use the School Configuration page to view/update connection strings

---

## FAQ

### How often does the sync run?

Automatically every day at **2:00 AM UTC**. You can also trigger manual syncs anytime.

### Does it delete student data?

No, it **deactivates** students by setting `IsActive = false` and `DeactivatedAt = [timestamp]`. The historical data remains in the database.

### What happens if Clever API is down during the scheduled sync?

The system automatically retries up to 5 times with exponential backoff. If all retries fail, you'll receive an alert and can manually trigger a sync later.

### Can I change the sync schedule?

Yes, but it requires changing the cron expression in the Azure Function and redeploying. Contact your IT administrator.

### How long does a sync take?

**Incremental sync:** Usually 30-60 seconds
**Full sync:** 1-3 minutes for 1500-2000 students

Actual time depends on network speed and Clever API responsiveness.

### What data is synced?

**Students:**
- Student ID (Clever ID)
- State ID
- Student Number
- Name (First, Middle, Last)
- Grade
- Gender
- Date of Birth
- Email
- Enrollment status

**Teachers:**
- Teacher ID (Clever ID)
- State ID
- Teacher Number
- Name (First, Middle, Last)
- Email
- Title
- Active status

### Can I sync just one school?

Yes! Use the manual sync with `?schoolId={id}` parameter.

### Is my data secure?

Yes:
- All credentials stored in Azure Key Vault
- Connections use TLS 1.2+ encryption
- Managed identity (no passwords in code)
- OAuth 2.0 authentication with Clever
- Role-based access control in Azure

### What if I add a new school?

**Using Admin Portal (Recommended):**
1. Navigate to Schools page
2. Follow your district's process for adding schools
3. Configure the school's Key Vault secrets via the Configuration page
4. Trigger a sync from the Sync Operations page

**Using CLI:**
1. Add the school to the `Schools` table in SessionDb
2. Create a database for the school
3. Add the school's connection string to Key Vault with pattern: `CleverSyncSOS--{SchoolPrefix}--ConnectionString`
4. Trigger a full sync for that school
5. The daily sync will include it going forward

### Can I see detailed logs?

**Admin Portal Users:**
- View sync history on the Sync Operations page
- Filter by school, status, and date range
- See detailed sync results including errors

**IT Administrators:**
- **Application Insights** (if configured) provides complete telemetry:
  - Every API call
  - Authentication attempts
  - Errors and warnings
  - Performance metrics
  - Sync statistics

---

## Getting Help

### Before Contacting Support

1. **Check the health endpoint** - Is the system showing healthy?
2. **Review recent sync history** - Are there error messages?
3. **Try a manual sync** - Does it work now?
4. **Check Application Insights** - Are there logged errors?

### What to Include When Reporting Issues

- **Timestamp** of when the issue occurred
- **Error message** from SyncHistory or health check
- **School ID** or District ID affected
- **What you were trying to do** (manual sync, checking status, etc.)
- **Screenshots** of error responses or health check results

### Contact Information

- **IT Support:** [Your IT support contact]
- **Azure Portal:** https://portal.azure.com
- **Clever Support:** https://support.clever.com

### Escalation Path

1. **Level 1:** Check this User Guide
2. **Level 2:** Contact your IT support team
3. **Level 3:** IT support reviews Application Insights and Azure logs
4. **Level 4:** Contact Clever support if issue is with their API

---

## Appendix: Quick Reference

### Common URLs

| Endpoint | Purpose | When to Use |
|----------|---------|-------------|
| `/health` | Overall system health | Daily morning check |
| `/health/clever-auth` | Authentication status | Troubleshooting auth issues |
| `/health/live` | Is app running? | Check if crashed |
| `/health/ready` | Can process requests? | Before triggering sync |
| `/api/sync` | Manual sync | Need immediate sync |

### Common SQL Queries

**Check last sync:**
```sql
SELECT TOP 1 * FROM SyncHistory ORDER BY StartedAt DESC;
```

**Count active students:**
```sql
SELECT COUNT(*) FROM Students WHERE IsActive = 1;
```

**Find recently deactivated students:**
```sql
SELECT * FROM Students
WHERE IsActive = 0 AND DeactivatedAt > DATEADD(DAY, -7, GETUTCDATE());
```

**Check sync success rate (last 7 days):**
```sql
SELECT
    CAST(StartedAt AS DATE) AS SyncDate,
    COUNT(*) AS TotalSyncs,
    SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS SuccessfulSyncs,
    SUM(TotalRecordsProcessed) AS TotalRecords
FROM SyncHistory
WHERE StartedAt > DATEADD(DAY, -7, GETUTCDATE())
GROUP BY CAST(StartedAt AS DATE)
ORDER BY SyncDate DESC;
```

---

## Related Documentation

- **[Admin Portal User Guide](AdminPortal-User-Guide.md)** - Web-based management (Recommended)
- **[Admin Portal Quick Start](AdminPortal-QuickStart.md)** - Deploying the Admin Portal
- **[Quick Start Guide](QuickStart.md)** - CLI-based deployment
- **[Configuration Setup](ConfigurationSetup.md)** - Manual configuration
- **[Naming Conventions](Naming-Conventions.md)** - Key Vault secret naming
- **[Security Architecture](SecurityArchitecture.md)** - Security design

---

**Document Version:** 2.0.0
**Last Updated:** 2025-11-25
**For Technical Documentation:** See `README.md` and `QuickStart.md`
**For Developer Documentation:** See `SpecKit/` folder
