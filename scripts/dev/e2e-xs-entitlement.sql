-- Disposable local E2E fixture authorized on 2026-10-15; NOT a payment or production upgrade.
-- Run with sqlcmd -b -S localhost,1433 -E -C -d LegacyVault_OfficialDev -i scripts/dev/e2e-xs-entitlement.sql
-- To restore, set @Restore = 1 after removing test asset/designation links through the API.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Restore bit = 0;
DECLARE @VaultId uniqueidentifier = '01a13ec1-ae9c-7c54-a092-05d245f53571';
DECLARE @EntitlementId uniqueidentifier = '01a13ec1-ae9d-71c8-a8ec-566620805d1f';
DECLARE @Expires datetimeoffset = '2026-10-22T16:30:00+07:00';

IF DB_NAME() <> N'LegacyVault_OfficialDev'
    THROW 51000, 'This fixture is restricted to LegacyVault_OfficialDev.', 1;
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT 1 FROM dbo.Vaults v WITH (UPDLOCK, HOLDLOCK)
    JOIN dbo.HandoverPlans p ON p.VaultId = v.Id
    WHERE v.Id = @VaultId AND v.Name = N'E2E Kho 2026-10-15' AND p.Status = N'Draft'
)
    THROW 51000, 'Expected E2E vault with Draft plan was not found; no changes allowed.', 1;

IF @Restore = 0
BEGIN
    IF @Expires <= SYSDATETIMEOFFSET()
        THROW 51000, 'Fixture period expired; review the test setup instead of silently extending it.', 1;
    UPDATE dbo.VaultEntitlements
    SET Tier = N'Xs', Status = N'PaidActive', StorageQuotaBytes = 209715200,
        IncludedBeneficiarySlots = 2, ExtraBeneficiarySlots = 0,
        ServiceExpiresAt = @Expires, UpdatedAt = SYSDATETIMEOFFSET()
    WHERE Id = @EntitlementId AND VaultId = @VaultId
        AND Tier = N'Free' AND Status = N'Free' AND StorageQuotaBytes = 20971520
        AND IncludedBeneficiarySlots = 0 AND ExtraBeneficiarySlots = 0
        AND ServiceExpiresAt IS NULL AND UpdatedAt IS NULL;
END
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.ContainerAssets ca JOIN dbo.Containers c ON c.Id = ca.ContainerId WHERE c.VaultId = @VaultId)
        OR EXISTS (SELECT 1 FROM dbo.Designations d JOIN dbo.Containers c ON c.Id = d.ContainerId WHERE c.VaultId = @VaultId)
        THROW 51000, 'Remove E2E container membership through the API before restoring Free.', 1;
    UPDATE dbo.VaultEntitlements
    SET Tier = N'Free', Status = N'Free', StorageQuotaBytes = 20971520,
        IncludedBeneficiarySlots = 0, ExtraBeneficiarySlots = 0,
        ServiceExpiresAt = NULL, UpdatedAt = NULL
    WHERE Id = @EntitlementId AND VaultId = @VaultId
        AND Tier = N'Xs' AND Status = N'PaidActive' AND StorageQuotaBytes = 209715200
        AND IncludedBeneficiarySlots = 2 AND ExtraBeneficiarySlots = 0 AND ServiceExpiresAt = @Expires;
END;
IF @@ROWCOUNT <> 1
    THROW 51000, 'Entitlement no longer matches the expected snapshot; no changes committed.', 1;
COMMIT;
SELECT VaultId, Tier, Status, StorageQuotaBytes, IncludedBeneficiarySlots,
    ExtraBeneficiarySlots, ServiceExpiresAt
FROM dbo.VaultEntitlements WHERE Id = @EntitlementId;
