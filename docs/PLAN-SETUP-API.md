# Executor invitation and plan activation

Implemented 15/10/2026. Canonical prefix is `/api/v1` only. Swagger UI: `/swagger/index.html`; OpenAPI: `/openapi/v1.json`.

| Method / path | Caller / body | Result |
|---|---|---|
| GET `/vaults/{vaultId}/plan-setup` | Owner Bearer | Status, configVersion, missingRequirements, policy/scope versions and text, invitation metadata |
| POST `/vaults/{vaultId}/executors` | Owner: email, role PRIMARY/BACKUP, optional beneficiaryId | 201 invitation metadata; no raw token |
| POST `/vaults/{vaultId}/executors/{executorId}/resend` | Owner: etag | Rotates token and expiry; old link invalid |
| POST `/executor-invitations/inspect` | Public: token | Minimal role/status/expiry/scope; no owner/vault content |
| POST `/executor-invitations/respond` | Verified active account: token, accept, acknowledgeScope, scopeVersion | Explicit accept/decline; Owner and second claimant rejected |
| POST `/vaults/{vaultId}/activate` | Owner: configVersion, policyVersion, acknowledgePolicy=true | Recheck requirements, atomically activate and open first period |

Paths in the table follow `/api/v1`. Request/response schemas are generated from DTOs in OpenAPI. FE validates wire responses with Zod.

Activation requires an active email-verified Owner, open vault, unexpired paid service, latest Passed Owner eKYC, a Ready container, and an active verified distinct primary Executor who accepted the current scope. If primary is also a beneficiary, an independent accepted non-beneficiary backup is required. Policy acknowledgement is explicit and stores version/time. A stale configVersion returns EDIT_CONFLICT. Duplicate activation does not reset deadlines. Deactivation suspends accepted Executors; reactivation requires fresh acceptance. Existing pending check-in settings apply when a new period starts.

Invitations expire after seven days. Only the token hash is persisted. The email link uses `/executor-invitation#token=...`; inspect/respond send the token in the JSON body. No token appears in API responses or cache keys. The token is a capability: a verified separate account explicitly claims it; invitation email alone does not bind an account. Do not forward links.

SMTP is opt-in: `ExecutorInvitations:DeliveryEnabled=true` plus existing configured Email:Smtp settings and App:ClientUrl. google-dev keeps delivery disabled. Null invitationSentAt means delivery is unconfirmed; the UI must not say mail was sent. Resend is explicit and rotates the previous link. Changing/revoking a pending assignment is not exposed by these endpoints; this checkpoint implements invite, resend and respond only.

Business failures use the existing ProblemDetails contract and traceId: EXECUTOR_INVITATION_INVALID (404), EXECUTOR_INVITATION_CONFLICT / EXECUTOR_NOT_ALLOWED / PLAN_ACTIVATION_NOT_ALLOWED / PLAN_ACTIVATION_REQUIREMENTS_NOT_MET (409), POLICY_ACKNOWLEDGEMENT_REQUIRED (400), plus existing VAULT_NOT_FOUND, PAID_TIER_REQUIRED and EDIT_CONFLICT. Requirements failures include missingRequirements. Serializable deadlock conflicts return EDIT_CONFLICT without blind mutation retries.

## Schema and dev rollout

Migration `20261015094749_AddPlanSetupConsent` adds four nullable fields: Executor invitationSentAt/acceptedScopeVersion and plan activationPolicyVersion/activationPolicyAcknowledgedAt. No existing rows are rewritten. Applied only to LegacyVault_OfficialDev after a COPY_ONLY backup and successful RESTORE VERIFYONLY WITH CHECKSUM (not a full restore drill). Backup is in the SQL instance's Backup directory, named `LegacyVault_OfficialDev_before_plan_setup_20261015_f0c2300581a741a6ba803997f844709a.bak`.

Rollback application binaries while retaining the additive nullable columns. Do not drop consent columns after live use; a destructive schema rollback requires a separate data recovery decision. Production rollout is not performed by this task.

## Verification

PlanSetupFlowTests exercise real HTTP/SQL against isolated GUID test databases, with synthetic paid/eKYC fixtures and an in-memory capturing email sender. They cover full invite/accept/activate/check-in, duplicates, concurrent activation, forced failure after a DB flush with full transaction rollback, missing requirements, expired service/token, stale policy/config, wrong Owner/claimant and backup independence. They do not verify a real eKYC provider or SMTP delivery. No real documents or external mail are used.
