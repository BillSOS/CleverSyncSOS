---
speckit:
  type: observability
  title: Clever API Authentication Observability Guide
  owner: Bill Martin
  version: 1.0.0
  linked_spec: ../../Specs/001-clever-api-auth/spec-1.md
  linked_plan: ../../Plans/001-clever-api-auth/plan.md
---

```

# Observability: Clever API Authentication and Connection

## Overview

This document defines the observability standards for the Clever API authentication subsystem. It ensures visibility into system health, token lifecycle, error conditions, and performance metrics using structured logging and Azure-native tools.

---

## 📊 Logging Standards [Phase: Health & Observability]

### Logging Framework

- Use `ILogger<T>` with structured logging
- Integrate with Azure Application Insights via `Microsoft.Extensions.Logging.ApplicationInsights`

### Required Log Events

| Event Name | Trigger | Properties |
| --- | --- | --- |
| `CleverAuthTokenAcquired` | Token successfully retrieved | `expiresAt`, `scope`, `retrievedAt` |
| `CleverAuthTokenRefreshed` | Token proactively refreshed | `expiresAt`, `refreshReason` |
| `CleverAuthFailure` | Token retrieval failed | `errorMessage`, `retryCount`, `timestamp` |
| `KeyVaultAccessFailure` | Key Vault access failed | `vaultUri`, `secretName`, `exceptionType` |
| `HealthCheckEvaluated` | Health check executed | `isHealthy`, `lastSuccessTimestamp`, `errorCount` |

### Log Hygiene

- No secrets or token values in logs
- Use `LogLevel.Information` for success, `LogLevel.Warning` for retries, `LogLevel.Error` for failures
- Include `CorrelationId` for traceability across services

---

## 🩺 Health Check Monitoring [Phase: Health & Observability]

### Endpoint

- `GET /health/clever-auth`

### Response Payload

```json
{
  "status": "Healthy",
  "lastSuccessTimestamp": "2025-11-08T14:32:00Z",
  "error": null
}
```

### Evaluation Logic

- Healthy if last token retrieval < 5 minutes ago and error count < 3
- Unhealthy if error count ≥ 3 or last success > 5 minutes ago

### Integration

- Register with ASP.NET Core health check middleware
- Configure Azure Monitor to alert on unhealthy status

---

## 📈 Telemetry and Metrics [Phase: Health & Observability]

### Application Insights Queries

| Metric | Query Example |
| --- | --- |
| Token acquisition success rate | `customEvents \| where name == "CleverAuthTokenAcquired"` |
| Token refresh frequency | `customEvents \| where name == "CleverAuthTokenRefreshed"` |
| Failure rate | `customEvents \| where name == "CleverAuthFailure"` |
| Health check uptime | `customMetrics \| where name == "HealthCheckEvaluated"` |

### Performance Targets

- Health check latency < 100ms
- Token acquisition < 5s
- 99.9% uptime for health check endpoint

---

## 🔔 Alerting Rules [Phase: Health & Observability]

| Condition | Action |
| --- | --- |
| Health check status = Unhealthy for 3+ minutes | Trigger Azure Monitor alert |
| Token acquisition fails 5 times in 10 minutes | Send warning to ops channel |
| Key Vault access fails repeatedly | Escalate to infrastructure team |

---