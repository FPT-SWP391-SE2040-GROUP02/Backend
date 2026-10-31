using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class EntitlementExpiryJob : IEntitlementExpiryJob
{
    /// <summary>Số kho tối đa mỗi lần quét; còn thì lần quét sau xử lý tiếp.</summary>
    private const int MaxVaultsPerScan = 200;

    private readonly IEntitlementExpiryRepository expiryRepository;
    private readonly IVaultRepository vaultRepository;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public EntitlementExpiryJob(
        IEntitlementExpiryRepository expiryRepository,
        IVaultRepository vaultRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.expiryRepository = expiryRepository;
        this.vaultRepository = vaultRepository;
        this.auditLogRepository = auditLogRepository;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<Guid>> FindExpiredVaultIdsAsync(CancellationToken ct)
    {
        return expiryRepository.FindExpiredPaidVaultIdsAsync(timeProvider.GetUtcNow(), MaxVaultsPerScan, ct);
    }

    public async Task<bool> ProcessVaultAsync(Guid vaultId, CancellationToken ct)
    {
        // Serializable như cấp quyền thanh toán: webhook gia hạn chạy cùng lúc thì một bên chờ bên kia,
        // không có chuyện job ghi đè GRACE_READ_ONLY lên kỳ vừa gia hạn.
        return await unitOfWork.ExecuteSerializableAsync(async () =>
        {
            Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
            if (vault == null)
            {
                return false;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            if (!EntitlementExpiryRules.ShouldStartGrace(vault.Entitlement, now))
            {
                return false;
            }

            EntitlementExpiryRules.StartGrace(vault.Entitlement, now);
            // OPLAN-01: ghi nhận kho chuyển sang ân hạn; hệ thống làm nên không có người thực hiện.
            auditLogRepository.Add(AuditRules.Create(vault.Id, null, AuditActorRole.System, AuditAction.ServiceGraceStarted,
                AuditTargetType.Vault, vault.Id, null, AuditOutcome.Succeeded, null, now));
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);
    }
}
