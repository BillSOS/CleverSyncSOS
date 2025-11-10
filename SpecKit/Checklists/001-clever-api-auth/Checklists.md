---
speckit:
  type: checklist
  title: Clever API Authentication Audit Checklist
  owner: Bill Martin
  version: 1.0.0
  linked_spec: ../../Specs/001-clever-api-auth/spec-1.md
  linked_plan: ../../Plans/001-clever-api-auth/plan.md
---

# Audit Checklist: Clever API Authentication

> **Purpose**: Use this checklist for pre-deploy, post-deploy, migration, and periodic audits. Each item has an explicit acceptance criterion and a short remediation step.

---

## Security and Secrets [Phase: Core Implementation]

- [ ] Azure Key Vault exists for production, staging, and devAcceptance: Vault URI configured in environment; secrets present; access policies set. Remediation: Create vault and add secrets; apply least-privilege access policy.
- [ ] Secrets stored: `CleverClientId`, `CleverClientSecret`Acceptance: Secrets present and named per `CredentialReference`. Remediation: Add secrets and rotate if found elsewhere.
- [ ] No credentials in source control or environment variablesAcceptance: Repo scan shows zero secrets; CI scan clean. Remediation: Remove secrets from repo, rotate, update configs to use Key Vault.
- [ ] Managed identity assigned and has `Secret Reader` role on Key VaultAcceptance: Managed identity appears in Key Vault access policies with Secret Reader. Remediation: Assign identity and re-run access test.
- [ ] TLS 1.2+ enforced on outbound HTTP clientsAcceptance: HTTP client configuration validates TLS >=1.2. Remediation: Force TLS version and re-deploy.

---

## Configuration and Validation [Phase: Core Implementation]

- [ ] `CleverAuthConfiguration` values present and validatedAcceptance: TokenEndpoint HTTPS, Scope non-empty, retry limits in range. Remediation: Adjust App Configuration values to meet validation rules.
- [ ] Retry policy parameters within allowed boundsAcceptance: MaxRetries 1–10; BaseDelaySeconds 1–60. Remediation: Update config in Azure App Configuration.
- [ ] Health check interval configured (10–300s)Acceptance: HealthCheckIntervalSeconds within bounds. Remediation: Update config to safe value.

---

## Authentication & Token Management [Phase: Core Implementation]

- [ ] First token acquired within 5s on startup (staging)Acceptance: Timestamp delta ≤ 5s. Remediation: Investigate Key Vault latency, network, or timeout settings.
- [ ] Proactive refresh triggers at ~75% lifetime and succeedsAcceptance: Refresh events in telemetry prior to expiry. Remediation: Check scheduler/IHostedService logic and token lifetime handling.
- [ ] No expired tokens used by requestsAcceptance: Requests authenticated only with valid tokens; logs show no 401 due to expired token. Remediation: Harden refresh logic and add guard checks.
- [ ] Token cached in memory only (no disk persistence)Acceptance: No disk writes for tokens. Remediation: Remove persistence and clear persisted data; redeploy.

---

## Resilience & Rate Limits [Phase: Core Implementation]

- [ ] Exponential backoff implemented (2s,4s,8s,16s,32s) or configurable equivalentAcceptance: Retry logs show exponential delays; policy applies to transient faults. Remediation: Update Polly policy configuration.
- [ ] 429 responses handled and respected with delay and retry windowsAcceptance: Logs show 429 detection and delayed retries. Remediation: Adjust retry on 429 to honor Retry-After header.
- [ ] Circuit breaker behavior prevents cascading failures (activations tracked)Acceptance: Circuit breaker activates under repeated failures and telemetry records state. Remediation: Tune thresholds in Polly configuration.

---

## Observability & Logging [Phase: Health & Observability]

- [ ] Required log events emitted: TokenAcquired, TokenRefreshed, AuthFailure, KeyVaultAccessFailure, HealthCheckEvaluatedAcceptance: Query returns events in Application Insights. Remediation: Instrument code to emit events and redeploy.
- [ ] No token values or secrets appear in logs or telemetryAcceptance: Search logs for secret patterns yields none. Remediation: Sanitize log messages and re-run audits.
- [ ] Health check endpoint `/health/clever-auth` returns expected payload and latency <100msAcceptance: Endpoint returns Healthy and latency measured ≤100ms. Remediation: Cache health, optimize check logic.
- [ ] Alerts configured for unhealthy health check, repeated token failures, and Key Vault access failuresAcceptance: Azure Monitor alerts exist and test notifications succeed. Remediation: Create alerts and test escalation path.

---

## Testing & CI/CD [Phase: Testing]

- [ ] Unit tests cover core auth logic, retry policy, and credential store (coverage baseline)Acceptance: CI test pass and coverage threshold met. Remediation: Add tests and enforce coverage in CI.
- [ ] Integration tests validate Key Vault access and token acquisition in ephemeral environmentsAcceptance: Integration tests use managed test identities and pass in CI. Remediation: Add or fix integration tests.
- [ ] CI pipeline includes security scans for secrets and static analysisAcceptance: Pipeline logs show scans and no critical findings. Remediation: Integrate scanners (secret-scan, SAST).
- [ ] CD pipeline warms app (token acquisition and health check) and runs post-deploy validationsAcceptance: Post-deploy step runs health check and verifies telemetry. Remediation: Add warm-up and post-deploy validation steps.

---

## Migration & Rollout [Phase: Deployment & Validation]

- [ ] Legacy credentials removed from all environments and code branchesAcceptance: Config scan and repo history show removal. Remediation: Remove and rotate credentials.
- [ ] Pilot tenants show stable token acquisition and refresh for 7+ days with metrics meeting benchmarksAcceptance: Benchmarks met for pilot window. Remediation: Pause rollout and remediate issues before broader rollout.
- [ ] Onboarding checklist completed per tenant (Key Vault, config, health)Acceptance: Onboarding artifacts recorded and validated. Remediation: Complete missing onboarding steps.

---

## Compliance & Audit Trails [Phase: Deployment & Validation]

- [ ] Key Vault access logs recorded and retained per policyAcceptance: Access logs available and match expected reads. Remediation: Ensure diagnostic settings for Key Vault are enabled.
- [ ] Audit of alerts and incidents performed after rollout and migrationAcceptance: Incident log indicates zero unresolved credential incidents. Remediation: Run audit and address findings.
- [ ] Secret rotation policy defined and executed (rotation cadence)Acceptance: Rotation schedule exists and last-rotation timestamp updated. Remediation: Implement rotation automation.

---

## Final Acceptance Gate [Phase: Deployment & Validation]

- [ ] All above items marked complete or have acceptable mitigations documentedAcceptance: Deployment allowed to production only when gate satisfied. Remediation: Address failures or apply formally approved mitigations.

---
