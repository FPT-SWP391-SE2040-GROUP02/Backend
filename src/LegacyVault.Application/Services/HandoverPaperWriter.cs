using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Dựng ảnh văn bản của một hồ sơ người nhận (văn bản từ chối hoặc biên bản đồng ý) và gắn vào hồ sơ.
/// </summary>
public interface IHandoverPaperWriter
{
    /// <summary>
    /// Dựng ảnh, lưu mã hóa và gán các cột Paper* của hồ sơ. Trả khóa lưu để người gọi xóa bù khi lưu bản ghi thất bại;
    /// trả null khi thiếu dữ liệu để dựng (không có hồ sơ người nhận, hoặc có reviewerId mà không tìm thấy Người xét duyệt).
    /// </summary>
    /// <remarks>
    /// reviewerId và reviewedAt để trống khi dựng lúc người nhận nộp; có giá trị khi dựng bù lúc duyệt cho hồ sơ cũ chưa có ảnh.
    /// </remarks>
    /// <remarks>
    /// Có document thì dựng từ biên bản đã ký (họ tên người nhận, số giấy chứng tử, ngày, chữ ký của người nhận và Người xét duyệt);
    /// không có thì dựng như luồng cũ từ hồ sơ người nhận và hồ sơ chứng tử.
    /// </remarks>
    Task<string?> WriteAsync(
        HandoverSubmission submission, HandoverTurn turn, Guid? reviewerId, DateTimeOffset? reviewedAt, CancellationToken ct,
        HandoverDocument? document = null);
}

public sealed class HandoverPaperWriter : IHandoverPaperWriter
{
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly IDeclinePaperRenderer renderer;
    private readonly IDeathCaseRepository deathCases;
    private readonly IAssetContentStore contentStore;
    private readonly IAdministrativeUnitCatalog administrativeUnits;
    private readonly IHandoverTemplateService? templates;

    public HandoverPaperWriter(
        IUserRepository users,
        IVaultRepository vaults,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        IDeclinePaperRenderer renderer,
        IDeathCaseRepository deathCases,
        IAssetContentStore contentStore,
        IAdministrativeUnitCatalog administrativeUnits,
        IHandoverTemplateService? templates = null)
    {
        this.templates = templates;
        this.users = users;
        this.vaults = vaults;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.renderer = renderer;
        this.deathCases = deathCases;
        this.contentStore = contentStore;
        this.administrativeUnits = administrativeUnits;
    }

    public async Task<string?> WriteAsync(
        HandoverSubmission submission, HandoverTurn turn, Guid? reviewerId, DateTimeOffset? reviewedAt, CancellationToken ct,
        HandoverDocument? document = null)
    {
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(turn.VaultId, turn.BeneficiaryId, ct);
        if (beneficiary == null && document == null)
        {
            return null;
        }
        User? reviewer = null;
        if (reviewerId != null)
        {
            reviewer = await users.FindByIdAsync(reviewerId.Value, ct);
            if (reviewer == null)
            {
                return null;
            }
        }
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        User? owner = null;
        if (vault != null)
        {
            owner = await users.FindByIdAsync(vault.OwnerId, ct);
        }

        DeclinePaperData data = new DeclinePaperData();
        data.IsAcceptance = submission.Kind == HandoverSubmissionKind.Accept;
        data.TurnCode = submission.TurnId.ToString("N").Substring(24).ToUpperInvariant();
        data.ContainerName = turn.ContainerName;
        data.OwnerFullName = owner?.FullName ?? string.Empty;
        BeneficiaryPersonalData? personal = beneficiary != null ? protector.Unprotect(beneficiary) : null;
        if (document != null)
        {
            data.OwnerFullName = document.OwnerFullName;
            data.DeclinerFullName = document.RecipientFullName;
        }
        else if (personal != null)
        {
            data.DeclinerFullName = personal.FullName;
        }
        data.CitizenId = personal?.CitizenId;
        data.CitizenIdLast4 = submission.CitizenIdLast4;
        data.SubmittedAt = submission.CreatedAt;
        data.NameMatches = submission.NameMatches;
        data.CitizenIdMatches = submission.CitizenIdMatches;
        data.DateOfBirthMatches = submission.DateOfBirthMatches;
        if (reviewer != null)
        {
            data.ReviewerFullName = reviewer.FullName;
            data.ReviewerLawyerCardNumber = reviewer.LawyerCardNumber;
        }
        if (reviewedAt != null)
        {
            data.ReviewedAt = reviewedAt.Value;
        }
        if (document != null)
        {
            data.DateOfDeath = document.DateOfDeath;
            data.DeathCertificateIssuedOn = document.CertificateIssuedOn;
            data.DeathRegistrationPlace = document.DeathRegistrationPlace;
            data.CertificateNumber = document.CertificateNumber;
            data.ItemCount = document.ItemCount;
            data.RecipientBirthYear = document.RecipientBirthYear;
            data.RecipientResidenceAddress = document.RecipientResidenceAddress;
            data.OwnerResidenceAddress = document.OwnerResidenceAddress;
            data.HasSignatures = true;
            data.RecipientSignedAt = document.RecipientSignedAt;
        }
        else
        {
            await FillDeathDatesAsync(data, turn.VaultId, ct);
        }

        await FillTemplateAsync(data, submission.Kind == HandoverSubmissionKind.Accept, document, ct);
        byte[] image = renderer.Render(data);
        StoredContent stored = await IdentityImageStorage.SaveAsync(contentStore, image, ct);
        submission.PaperStorageKey = stored.StorageKey;
        submission.PaperSizeBytes = stored.SizeBytes;
        submission.PaperEncryptionKeyId = stored.EncryptionKeyId;
        submission.PaperWrappedDataKey = stored.WrappedDataKey;
        return stored.StorageKey;
    }

    /// <summary>
    /// Điền các đoạn chữ của mẫu vào dữ liệu dựng: biên bản đã tạo dùng đúng mẫu ghi trên biên bản (templateVersion), luồng cũ không có
    /// biên bản thì dùng mẫu đang áp dụng. Không có dịch vụ mẫu thì để trống để bộ dựng dùng chữ cố định.
    /// </summary>
    private async Task FillTemplateAsync(DeclinePaperData data, bool isAcceptance, HandoverDocument? document, CancellationToken ct)
    {
        if (templates == null)
        {
            return;
        }
        string key = await templates.ActiveVersionKeyAsync(ct);
        if (document != null)
        {
            key = document.TemplateVersion;
        }
        ResolvedHandoverTemplate template = await templates.ResolveAsync(key, ct);
        data.DeclineNotice = template.Content.DeclineNotice;
        if (isAcceptance)
        {
            data.Paragraphs = template.Content.Accept;
        }
        else
        {
            data.Paragraphs = template.Content.Decline;
        }
    }

    /// <summary>Ngày mất, ngày đăng ký khai tử và nơi đăng ký in lên văn bản; không có hồ sơ hoặc chưa đủ dữ liệu thì để trống.</summary>
    private async Task FillDeathDatesAsync(DeclinePaperData data, Guid vaultId, CancellationToken ct)
    {
        DeathCase? deathCase = await deathCases.FindOpenByVaultIdAsync(vaultId, ct);
        if (deathCase == null || deathCase.ReviewVersionNo == null)
        {
            return;
        }
        DeathCaseVersion version = DeathCaseReviewRules.FindReviewVersion(deathCase);
        data.DateOfDeath = version.DateOfDeath;
        data.DeathCertificateIssuedOn = version.IssuedOn;
        data.DeathRegistrationPlace = administrativeUnits.BuildRegistrationPlace(
            version.DeathRegistrationProvinceCode, version.DeathRegistrationWardCode);
    }
}
