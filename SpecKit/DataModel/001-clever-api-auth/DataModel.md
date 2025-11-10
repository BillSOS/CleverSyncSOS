---
speckit:
  type: data-model
  title: Clever API Authentication Data Model
  owner: Bill Martin
  version: 1.0.0
  linked_spec: ../../Specs/001-clever-api-auth/spec-1.md
  linked_plan: ../../Plans/001-clever-api-auth/plan.md
---

# Data Model: Clever API Authentication and Connection

## Overview

This document defines the key data entities used in the Clever API authentication feature. These models support secure credential handling, token lifecycle management, health monitoring, and configuration flexibility.

---

## 🧩 Entities

### 1. `CleverAuthConfiguration` [Phase: Core Implementation]

**Purpose**: Holds configuration values for authentication behavior and retry logic.

| Field | Type | Description |
| --- | --- | --- |
| `TokenEndpoint` | string | URL for Clever OAuth token exchange |
| `Scope` | string | OAuth scope string |
| `RetryPolicy` | object | Contains retry settings |
| `RetryPolicy.MaxRetries` | int | Maximum number of retries |
| `RetryPolicy.BaseDelaySeconds` | int | Initial delay for exponential backoff |
| `HealthCheckIntervalSeconds` | int | Interval for refreshing health status |

---

### 2. `CleverAuthToken` [Phase: Core Implementation]

**Purpose**: Represents an access token retrieved from Clever.

| Field | Type | Description |
| --- | --- | --- |
| `AccessToken` | string | OAuth token value |
| `ExpiresAt` | DateTime | UTC expiration timestamp |
| `Scope` | string | Scope granted by Clever |
| `TokenType` | string | Typically "Bearer" |
| `RetrievedAt` | DateTime | Timestamp when token was acquired |

---

### 3. `AuthenticationHealthStatus` [Phase: Health & Observability]

**Purpose**: Tracks the health of the authentication subsystem.

| Field | Type | Description |
| --- | --- | --- |
| `IsHealthy` | bool | Indicates if auth is functioning |
| `LastSuccessTimestamp` | DateTime | Last successful token retrieval |
| `LastErrorMessage` | string | Most recent error (if any) |
| `ErrorCount` | int | Number of consecutive failures |
| `LastCheckedAt` | DateTime | Timestamp of last health check |

---

### 4. `CredentialReference` [Phase: Core Implementation]

**Purpose**: Points to credential locations in Azure Key Vault.

| Field | Type | Description |
| --- | --- | --- |
| `ClientIdSecretName` | string | Key Vault secret name for Client ID |
| `ClientSecretName` | string | Key Vault secret name for Client Secret |
| `VaultUri` | string | URI of the Azure Key Vault instance |

---

## 🔄 Relationships

- `CleverAuthConfiguration` is injected into the authentication service via DI.
- `CleverAuthToken` is produced by the authentication service and cached in memory.
- `AuthenticationHealthStatus` is updated by the health check service and exposed via `/health/clever-auth`.
- `CredentialReference` is used by `KeyVaultCredentialStore` to locate secrets.
