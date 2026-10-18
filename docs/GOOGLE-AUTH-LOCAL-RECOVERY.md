# Google Auth local — schema recovery proposal

Status: fresh database and explicit `google-dev` launch profile verified; user confirmed real Google login reaches dashboard. Date: 2026-10-15.
Branch: `fix/google-auth-login`, created from `develop` at the user's request before changes. On 2026-10-15 the user authorized committing the Backend changes and explicitly allowed a Conventional Commit without a Jira key. Push/merge/deploy are outside that authorization.

## Evidence

- The running local API is the official Backend Debug executable. A deliberately malformed Google token returns HTTP 401, without creating a Google account or session.
- Development configuration includes User Secrets (`legacyvault-api-dev`). Its effective `ConnectionStrings:Default` selects a local SQL instance different from the checked-in development default. Connection strings and credentials were not printed.
- The effective `LegacyVault` database has only `Users`, `RefreshTokens` and `__EFMigrationsHistory`.
- Its migration history is `20261011013702_InitialCreate`, `20261011141731_AddUserRowVersion`, rather than the official Backend migrations beginning with `20261013145252_InitialCreate`.
- Missing Auth schema includes `ExternalLogins`, `ExternalLoginLinkTickets`, `LoginThrottles`, `Users.EmailVerifiedAt` and `RefreshTokens.SessionStartedAt`.
- Calling the real `ExternalLoginRepository.FindBySubjectAsync` with a synthetic nonexistent subject reproduces SQL Server error 208 (`SqlException`, invalid object). This is the first database query in Google login after token verification. No real credential or account data was used in the probe.
- All nine official migrations are pending against this database. Applying InitialCreate directly would collide with existing tables; do not reset it, rewrite its migration history, or run the migration script against it.
- Further SQL session metadata by API PID showed the Visual Studio host actually used the Docker SQL connection from appsettings.Development (`127.0.0.1,14331`, LegacyVault), while the standalone diagnostic/Release host used User Secrets (`localhost,1433`, Windows authentication). Initial attribution to User Secrets as the active Visual Studio connection was incorrect. Both existing database configurations lacked the current Auth schema; the explicit launch profile below removes this ambiguity.

## Proposed recovery and six-part plan

1. Contract: existing Google login/link-confirm endpoints and DTOs remain unchanged. Correct the local database configuration rather than masking HTTP 500 in the frontend.
2. Order: validate a fresh local target → apply official EF migrations there → verify Auth schema and repository read → update Development connection override → restart the API → browser Google login/link/session checks.
3. CREATE: this recovery note and a new local development database `LegacyVault_OfficialDev` (only after approval). MODIFY: the Development connection override in User Secrets, preserving server/credentials and the old connection for rollback without exposing it. DELETE: none. No new runtime dependency or application business code is needed based on current evidence.
4. Tasks: reject an existing target database rather than reusing/overwriting it; create it on the same validated local SQL instance; run current official migrations; compare pending migrations/schema; verify synthetic Google repository lookup; switch only the selected local API configuration and restart it with the user's cooperation if Visual Studio owns the process.
5. Recovery: existing `LegacyVault` remains intact, including accounts/data. The new database initially has no old accounts. To roll back the configuration, restore the previous Development connection and restart the API. Do not automatically delete either database. Migrating existing accounts is a separate data migration requiring a backup, mapping and explicit approval.
6. DoD: no pending official migrations in the new database; Auth tables/columns present; Google repository synthetic lookup succeeds; malformed token still returns 401; real Google login or link-confirm succeeds and reload/logout behave correctly. Browser checks require the user's chosen Google test account; fake identities do not prove Google verification.

## Prepared review artifact

The workspace diagnostic helper `scratch/GoogleAuthDiagnostic.cs` (relative to the workspace root, outside this repository) only reads metadata and performs the synthetic lookup. It generated `scratch/google-auth-new-database.sql` from the official EF migrations for review. The SQL has **not** been applied. Use it only for a fresh database; do not run it on the existing legacy schema.

## Approved recovery executed

The user explicitly approved creating the fresh local database and switching Backend on 2026-10-15. The provisioning helper first verified a local SQL instance and rejected an already-existing target, then applied all nine official migrations to `LegacyVault_OfficialDev`. Pending migrations: zero. The real Google repository lookup with a synthetic nonexistent subject now succeeds.

The User Secrets file was backed up in its existing User Secrets directory before changing only `ConnectionStrings:Default` to the new database, preserving server/authentication settings and all unrelated values. The workspace `scratch/google-auth-dev-manifest.json` records the rollback backup path and database names without credentials. The old `LegacyVault` database was not changed or deleted. Do not put the User Secrets backup in Git or print its contents.

Restarting the Visual Studio host alone still returned HTTP 500 because it selected a different SQL instance. Added a native `google-dev` launch profile with an explicit public local Windows-authenticated connection to `localhost,1433 / LegacyVault_OfficialDev`, port 5000 and Development environment. It contains no password and does not change the standard Docker `http` profile. Check-in/handover background jobs are disabled in this Auth verification profile.

When port 5000 became free, started the API with this profile. HTTP checks now return 401 `INVALID_GOOGLE_TOKEN` for a malformed Google token and 400 `INVALID_LINK_TICKET` for a synthetic missing link ticket, rather than HTTP 500. The separate diagnostic host on port 5001 was stopped; the API on port 5000 remains running for the user. No user-owned process was stopped by the agent.

The user then retried Google in Chrome and reported reaching dashboard. This is user-confirmed real Google login, distinct from automated fake-token/SQL checks. Real Google link-confirm for an existing app account and reload/logout after Google login were not independently exercised in this follow-up.

Post-login aggregate SQL checks found one user and one Google external login in the new database; no email, subject, password or token values were read or printed. Current Auth schema has no missing columns and both Google repository reads succeed.

Release build PASS (0 warnings/errors); targeted GoogleAuth/AuthHttpSecurityTests: 31 PASS, 0 SKIP, 0 FAIL. No application business code was changed and no commit/push was performed.

## Run this verified local configuration

In Visual Studio select `LegacyVault.Api` as startup project, then select **google-dev** next to Start. Or, from the Backend repository:

```powershell
dotnet run --project src/LegacyVault.Api --launch-profile google-dev
```

Only one API process may listen on port 5000. This profile requires the local Windows SQL Server instance on port 1433 and the provisioned database. The unchanged `http` profile targets the Docker setup; it does not automatically select this new database. The usual Docker database must separately have the official schema before using Google/Auth there.

### Debugger pauses that look like FE network failures

For browser E2E, select `google-dev` and use **Ctrl+F5 / Start Without Debugging**, or the terminal command above. Stop the existing host before starting another on port 5000.

Visual Studio can pause on a thrown `LegacyVault.Application.Exceptions.AppException` before the global exception handler runs. While paused, even `/health/live` can time out. An account without a vault legitimately produces `404 VAULT_NOT_FOUND`; adding an asset to a Free container produces `403 PAID_TIER_REQUIRED`. These responses must reach the FE rather than being replaced with successful empty results.

If debugging, continue with F5 and adjust the debugger's **Break when this exception type is thrown** setting for this application exception as needed. This changes debugger behavior only; keep application authorization, exception middleware and HTTP status codes intact. A FE request that already timed out needs a manual retry after the host resumes; do not automatically retry mutations.
