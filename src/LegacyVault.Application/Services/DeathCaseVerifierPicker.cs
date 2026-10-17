using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <remarks>
/// ROLE-03 vế người nhận (D1): "người nhận của kho" là người có chỉ định trong gói READY, đúng tập sẽ vào snapshot
/// lúc duyệt; trong lúc hồ sơ đang xét Owner không sửa được người nhận (AC-103) nên tập này ổn định.
/// Nhận ra bằng email (đã giải mã, chuẩn hóa); danh sách email được truyền xuống repository để lọc trong DB.
/// Giới hạn: người nhận không khai email, hoặc tạo tài khoản bằng email khác, thì chưa bắt được trước F3;
/// không loại theo ClaimantUserId (chỉ có sau khi duyệt). Chờ chức năng C (danh tính đã gắn) để so theo CCCD.
/// Không loại người từng giữ hồ sơ trước đó (chỉ loại người đang giữ) để nhóm chỉ có 2 Verifier vẫn chuyền được (D5).
/// </remarks>
public sealed class DeathCaseVerifierPicker : IDeathCaseVerifierPicker
{
    private readonly IDeathCaseRepository deathCaseRepository;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverRepository handoverRepository;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;

    public DeathCaseVerifierPicker(
        IDeathCaseRepository deathCaseRepository,
        IExecutorReader executorReader,
        IHandoverRepository handoverRepository,
        IBeneficiaryDataProtector beneficiaryProtector)
    {
        this.deathCaseRepository = deathCaseRepository;
        this.executorReader = executorReader;
        this.handoverRepository = handoverRepository;
        this.beneficiaryProtector = beneficiaryProtector;
    }

    public async Task<Guid?> PickAsync(
        Vault vault, Guid submitterUserId, Guid? currentVerifierUserId, CancellationToken ct)
    {
        List<Guid> excluded = new List<Guid>();
        excluded.Add(vault.OwnerId);
        excluded.Add(submitterUserId);
        if (currentVerifierUserId != null)
        {
            excluded.Add(currentVerifierUserId.Value);
        }

        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId != null)
            {
                excluded.Add(executor.UserId.Value);
            }
        }

        // ROLE-03: loại Verifier trùng email người nhận của kho.
        List<string> excludedEmails = await GetBeneficiaryEmailsAsync(vault.Id, ct);

        Guid? verifierId = await deathCaseRepository.FindVerifierCandidateAsync(excluded, excludedEmails, ct);
        return verifierId;
    }

    public async Task<List<string>> GetBeneficiaryEmailsAsync(Guid vaultId, CancellationToken ct)
    {
        List<string> emails = new List<string>();
        List<Designation> designations = await handoverRepository.FindStartableDesignationsAsync(vaultId, ct);
        foreach (Designation designation in designations)
        {
            BeneficiaryPersonalData data = beneficiaryProtector.Unprotect(designation.Beneficiary);
            if (string.IsNullOrWhiteSpace(data.Email))
            {
                continue;
            }
            string email = DeathCaseAssignmentRules.NormalizeEmail(data.Email);
            if (!emails.Contains(email))
            {
                emails.Add(email);
            }
        }
        return emails;
    }
}
