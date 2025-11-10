---
speckit:
  type: validation
  title: Clever API Authentication Validation Rules
  owner: Bill Martin
  version: 1.0.0
  linked_spec: ../Specs/001-clever-api-auth/spec-1.md
  linked_data_model: ../DataModel/001-clever-api-auth/DataModel.md
---

# Validation Rules: Clever API Authentication and Connection

> **Purpose**: This document extends the data model with concrete validation rules for each entity. These rules ensure runtime safety, configuration integrity, and alignment with the CleverSyncSOS Constitution.

---

## Entity: `CleverAuthConfiguration` [Phase: Core Implementation]

| Field | Rule | Description |
| --- | --- | --- |
| `TokenEndpoint` | Must be a valid HTTPS URL | Enforces secure endpoint |
| `Scope` | Must be non-empty and space-delimited | Required for Clever API token |
| `RetryPolicy.MaxRetries` | Integer ≥ 1 and ≤ 10 | Prevents runaway retry loops |
| `RetryPolicy.BaseDelaySeconds` | Integer ≥ 1 and ≤ 60 | Controls exponential backoff |
| `HealthCheckIntervalSeconds` | Integer ≥ 10 and ≤ 300 | Ensures reasonable health polling cadence |

---

## Entity: `CleverAuthToken` [Phase: Core Implementation]

| Field | Rule | Description |
| --- | --- | --- |
| `AccessToken` | Must be non-empty string | Required for API calls |
| `ExpiresAt` | Must be in UTC and > `RetrievedAt` | Ensures token is valid |
| `Scope` | Must match requested scope | Verifies token permissions |
| `TokenType` | Must be "Bearer" | Required by Clever API |
| `RetrievedAt` | Must be in UTC | Standardized timestamp format |

---

## Entity: `AuthenticationHealthStatus` [Phase: Health & Observability]

| Field | Rule | Description |
| --- | --- | --- |
| `IsHealthy` | Boolean | Indicates system status |
| `LastSuccessTimestamp` | Must be in UTC | Used for uptime tracking |
| `LastErrorMessage` | Optional; max length 500 chars | Prevents log overflow |
| `ErrorCount` | Integer ≥ 0 | Tracks consecutive failures |
| `LastCheckedAt` | Must be in UTC | Timestamp of last health update |

---

## Entity: `CredentialReference` [Phase: Core Implementation]

| Field | Rule | Description |
| --- | --- | --- |
| `ClientIdSecretName` | Must match Key Vault naming conventions | Ensures retrievability |
| `ClientSecretName` | Must match Key Vault naming conventions | Ensures retrievability |
| `VaultUri` | Must be valid Azure Key Vault URI | Required for access via SDK |

---

## Cross-Entity Rules [Phase: Core Implementation & Health & Observability]

- `ExpiresAt - RetrievedAt` must be ≥ 5 minutes to avoid short-lived tokens.
- Health check must fail if `ErrorCount` ≥ 3 and `LastSuccessTimestamp` > 5 minutes ago.
- Retry logic must not trigger if `IsHealthy == false` and `ErrorCount` ≥ 5 (circuit breaker).

---

