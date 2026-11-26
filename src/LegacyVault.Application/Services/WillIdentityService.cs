using LegacyVault.Application.Dtos;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IWillIdentityService
{
    /// <summary>
    /// Chủ tài sản xem danh tính đầy đủ (đã giải mã) của mình và của từng người nhận trong kho để lập di chúc.
    /// Người khác (kể cả Admin) → 404 như các API Chủ tài sản. Mỗi lần thành công ghi nhật ký WILL_IDENTITY_VIEWED
    /// (không ghi giá trị đã giải mã).
    /// </summary>
    Task<WillIdentityResponse> GetAsync(Guid vaultId, CancellationToken ct);
}

public sealed class WillIdentityService : IWillIdentityService
{
    private const int BeneficiaryPageSize = 100;

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IOwnerIdentityRepository ownerIdentities;
    private readonly IOwnerIdentityProtector ownerProtector;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider time;

    public WillIdentityService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IOwnerIdentityRepository ownerIdentities,
        IOwnerIdentityProtector ownerProtector,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector beneficiaryProtector,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        TimeProvider time)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.ownerIdentities = ownerIdentities;
        this.ownerProtector = ownerProtector;
        this.beneficiaries = beneficiaries;
        this.beneficiaryProtector = beneficiaryProtector;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.time = time;
    }

    public async Task<WillIdentityResponse> GetAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault vault = VaultRules.EnsureOwnedBy(await vaults.FindByIdAsync(vaultId, ct), userId);

        // Chủ tài sản chưa có danh tính eKYC đã gắn thì owner = null.
        WillOwnerIdentityResponse? owner = null;
        OwnerIdentity? ownerIdentity = await ownerIdentities.FindByUserIdAsync(vault.OwnerId, ct);
        if (ownerIdentity != null)
        {
            OwnerIdentityData data = ownerProtector.Unprotect(ownerIdentity);
            owner = new WillOwnerIdentityResponse(data.FullName, data.DateOfBirth, data.CitizenId, data.ResidenceAddress);
        }

        List<WillRecipientIdentityResponse> recipients = new List<WillRecipientIdentityResponse>();
        int pageIndex = 1;
        int totalCount = 0;
        int readCount = 0;
        do
        {
            (List<Beneficiary> Items, int TotalCount) page = await beneficiaries.FindPageByVaultIdAsync(
                vault.Id, null, pageIndex, BeneficiaryPageSize, true, ct);
            totalCount = page.TotalCount;
            readCount = readCount + page.Items.Count;
            foreach (Beneficiary beneficiary in page.Items)
            {
                BeneficiaryPersonalData personal = beneficiaryProtector.Unprotect(beneficiary);
                recipients.Add(new WillRecipientIdentityResponse(
                    beneficiary.Id, personal.FullName, personal.DateOfBirth, personal.CitizenId));
            }
            if (page.Items.Count == 0)
            {
                break;
            }
            pageIndex++;
        }
        while (readCount < totalCount);

        // Ghi nhật ký lần xem; không đưa họ tên/CCCD vào nhật ký (SEC-07).
        audit.Add(AuditRules.OwnerAction(vault.Id, userId, AuditAction.WillIdentityViewed,
            AuditTargetType.Vault, vault.Id, null, time.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(ct);

        return new WillIdentityResponse(owner, recipients);
    }
}
