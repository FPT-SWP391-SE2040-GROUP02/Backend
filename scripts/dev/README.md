# Local E2E entitlement fixture

User-authorized development setup, 2026-10-15. This is test data provisioning, not payment verification. It must never run during startup, migration, deployment or against another database.

## Scope and execution

Contract: existing vault/container/beneficiary APIs; no new endpoint, role, auth bypass or payment record. Order: read original entitlement → apply guarded fixture → create synthetic beneficiary through UI → attach existing asset and beneficiary through UI → reload → read SQL metadata. CREATE: this script/runbook and one synthetic beneficiary; MODIFY: the named E2E entitlement and container membership; DELETE: none. DoD: XS/2 slots visible to the FE, container Ready after reload, exactly one asset and one recipient, plan still Draft. Failure recovery: stop on a mismatched snapshot, do not retry UI mutations until reading current data.

Target: `localhost,1433 / LegacyVault_OfficialDev`, Windows authentication. Vault `01a13ec1-ae9c-7c54-a092-05d245f53571`, named `E2E Kho 2026-10-15`, owned by the existing signed-in development account. No new login credentials are created.

From Backend:

```powershell
sqlcmd -b -S localhost,1433 -E -C -d LegacyVault_OfficialDev -i scripts/dev/e2e-xs-entitlement.sql
```

The script is scoped to the exact database, vault/name, Draft plan and entitlement ID. A transaction and expected original values prevent overwriting an existing changed entitlement. It follows `ApiTestHelpers.UpgradeVaultToXsAsync`: XS/PaidActive, 200 MiB, 2 included recipient slots, 0 extras; this fixture additionally expires **2026-10-22 16:30 UTC+07**. Expiry is a configured test field; this run does not verify automatic expiry processing.

## Original snapshot and recovery

Entitlement `01a13ec1-ae9d-71c8-a8ec-566620805d1f`: Tier Free, Status Free, quota 20971520, included slots 0, extras 0, ServiceExpiresAt null, UpdatedAt null. CreatedAt and user identity are unchanged.

To restore: first remove the E2E asset/recipient links through normal container APIs/UI; leave the plan Draft. Set `@Restore = 1` in the script, review it, then run the same command. Restoration refuses existing links or a mismatched paid entitlement. It restores only the entitlement snapshot, not the later test assets/beneficiary/container data. No reset, deletion, credential change or migration is required. Restore was not executed because the paid setup is needed for continued testing.

## Verified runtime result

- Applied once successfully; reapplication rejected by the snapshot guard as expected.
- Created synthetic adult recipient `E2E Người nhận giả lập`, ID `01a13edf-fd80-7abf-b7e5-16d262df53c5`, identity masked as `****1015`, contact under reserved `example.test`. No real identity document/contact or invitation delivery was used.
- Existing text asset `01a13ec2-33e3-7c01-b2c8-9e460174c031` attached through FE to container `01a13ec2-76d4-7a40-bf2c-f79c5e053467`.
- Attached the recipient through FE; server changed the container Draft → Ready. Reload retained the signed-in session and container data.
- SQL metadata confirmed container Ready, 1 asset link, 1 designation, **1/2 slots used**, plan Draft and ActivatedAt null. No claims/decisions/download grants or handover execution were created.
- Vite stopped during the run; restarted the development server on the verified empty port 5173. A fresh browser tab recovered the dev module loading failure. Backend on 5000 was not stopped or replaced.
- No application source/dependency changed; no new build or unit suite needed for this SQL fixture/runbook. Verification is real browser/API/SQL plus the script reapplication guard. Rollback and negative slot-limit/concurrency scenarios were not exercised.
