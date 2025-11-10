```
---
speckit:
  type: constitution
  title: CleverSyncSOS Constitution
  owner: Bill Martin
  version: 1.1.0
---

```

# CleverSyncSOS Constitution

## Purpose

CleverSyncSOS enables secure, automated synchronization between school and district SIS data via Clever’s API and individual school Azure SQL databases. It ensures consistency, accuracy, and timeliness by syncing data such as students, teachers, class schedules, and attendance records from Clever to the SOS database at configurable intervals.

## Scope

This constitution defines the guiding principles, governance, and architectural responsibilities of the CleverSyncSOS system. It serves as the foundation for all future specifications, plans, and tasks.

## Audience

Primary users are SOS technical staff responsible for deploying, configuring, and maintaining synchronization services for partner schools.

## Governance

- **Owner**: Bill Martin
- **Repository**: `CleverSyncSOS` on GitHub
- **Architecture Decision Authority**: System Architect (Bill Martin)
- **Change Process**: Pull Request approval required by repository owner
- **Development Lifecycle**: Spec-Driven Development (SDD); all functional changes must first update the relevant spec

## Principles

1. **Security First**: OAuth tokens, Client Secrets, and District/School IDs must be stored in Azure Key Vault.
2. **Scalability**: Supports multiple schools per district via dynamic Azure Functions.
3. **Isolation**: Each school database is separate; shared code runs from centralized function apps.
4. **Observability**: Logs are centralized using Azure Application Insights and follow Azure best practices.
5. **Configurability**: Synchronization intervals and retry behavior must be externally configurable.
6. **Compatibility**: Maintain alignment with the latest Clever API and handle versioning and rate limits gracefully.

## Technical Foundation

- **Framework**: .NET 9
- **Language**: C#
- **Database**: Azure SQL
- **ORM**: Entity Framework Core
- **Cloud Environment**: Microsoft Azure (App Service or Functions)
- **IDE**: Visual Studio
- **Logging**: .NET abstractions with Azure-integrated providers
- **Testing**: Visual Studio test projects with CI integration
- **Optional**: Microsoft Aspire for orchestration/observability if beneficial

## Development Standards

- Follow .NET coding conventions and async patterns.
- Use dependency injection for all services and configuration.
- Store configuration in Azure App Configuration or secure settings.
- Write automated tests for core sync logic and error handling.
- Use EF Core for all data access.
- Ensure logs are contextual and actionable.

## Maintenance & Operations

- Source control via GitHub with standard branching and PR review.
- CI/CD pipelines for Azure deployment.
- Regular review of logs and telemetry to ensure data integrity and system reliability.

* * *

## 🔖 Flagged for Other Speckit Documents

These items are better suited for other Speckit types:

| Item | Suggested Speckit Type | Reason |
| --- | --- | --- |
| “At predetermined intervals…” (sync behavior) | `spec` | Describes functional behavior, not constitutional principle |
| “Data such as students, teachers…” | `spec` | Defines data scope and sync logic |
| “Support flexible configuration of sync intervals” | `plan` or `spec` | Operational detail, not a guiding principle |
| “CI/CD pipelines will be established…” | `plan` | Implementation roadmap detail |
| “Automated tests must be written…” | `task` or `spec` | Enforceable engineering requirement |

* * *
