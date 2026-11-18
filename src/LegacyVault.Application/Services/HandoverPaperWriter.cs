using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>Dựng ảnh văn bản (biên bản đồng ý hoặc văn bản từ chối) của một hồ sơ người nhận rồi lưu mã hóa.</summary>
public sealed class HandoverPaperWriter : IHandoverPaperWriter
{
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly IHandoverPaperRenderer renderer;
    private readonly IDeathCaseRepository deathCases;
    private readonly IAssetContentStore contentStore;
    private readonly IAdministrativeUnitCatalog administrativeUnits;

    // Luôn có khi chạy thật; chỉ một số test bỏ trống (null) thì bộ dựng dùng chữ cố định.
    private readonly IHandoverTemplateService? templates;

    public HandoverPaperWriter(
        IUserRepository users,
        IVaultRepository vaults,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        IHandoverPaperRenderer renderer,
        IDeathCaseRepository deathCases,
        IAssetContentStore contentStore,
        IAdministrativeUnitCatalog administrativeUnits,
        IHandoverTemplateService? templates = null)
    {
        this.users = users;
        this.vaults = vaults;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.renderer = renderer;
        this.deathCases = deathCases;
        this.contentStore = contentStore;
        this.administrativeUnits = administrativeUnits;
        this.templates = templates;
    }

    /// <summary>Dựng ảnh văn bản PNG của hồ sơ, mã hóa, lưu và ghi tham chiếu vào các cột Paper* của hồ sơ.</summary>
    public async Task<string?> WriteAsync(HandoverPaperRequest request, CancellationToken ct)
    {
        HandoverSubmission submission = request.Submission;
        HandoverTurn turn = request.Turn;
        HandoverDocument? document = request.Document;

        // 1. Người nhận do Chủ kho khai; không có và cũng không có biên bản thì không đủ dữ liệu để dựng
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(turn.VaultId, turn.BeneficiaryId, ct);
        if (beneficiary == null && document == null)
        {
            return null;
        }

        // 2. Người xét duyệt (chỉ khi dựng lúc duyệt); không tìm thấy thì không dựng
        User? reviewer = null;
        if (request.ReviewerId != null)
        {
            reviewer = await users.FindByIdAsync(request.ReviewerId.Value, ct);
            if (reviewer == null)
            {
                return null;
            }
        }

        // 3. Tên Chủ kho theo tài khoản
        string ownerName = string.Empty;
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault != null)
        {
            User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
            if (owner != null)
            {
                ownerName = owner.FullName;
            }
        }

        // 4. Điền dữ liệu in chung cho cả hai loại văn bản
        HandoverPaperData data = new HandoverPaperData
        {
            IsAcceptance = submission.Kind == HandoverSubmissionKind.Accept,
            TurnCode = submission.TurnId.ToString("N").Substring(24).ToUpperInvariant(),
            ContainerName = turn.ContainerName,
            OwnerFullName = ownerName,
            CitizenIdLast4 = submission.CitizenIdLast4,
            SubmittedAt = submission.CreatedAt,
            NameMatches = submission.NameMatches,
            CitizenIdMatches = submission.CitizenIdMatches,
            DateOfBirthMatches = submission.DateOfBirthMatches
        };
        BeneficiaryPersonalData? personal = null;
        if (beneficiary != null)
        {
            personal = protector.Unprotect(beneficiary);
            data.CitizenId = personal.CitizenId;
        }
        if (reviewer != null)
        {
            data.ReviewerFullName = reviewer.FullName;
            data.ReviewerLawyerCardNumber = reviewer.LawyerCardNumber;
        }
        if (request.ReviewedAt != null)
        {
            data.ReviewedAt = request.ReviewedAt.Value;
        }

        // 5. Dữ liệu riêng: có biên bản thì lấy từ biên bản đã ký, không thì lấy từ hồ sơ người nhận và hồ sơ chứng tử
        if (document != null)
        {
            data.OwnerFullName = document.OwnerFullName;
            data.RecipientFullName = document.RecipientFullName;
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
            if (personal != null)
            {
                data.RecipientFullName = personal.FullName;
            }
            await FillDeathDatesAsync(data, turn.VaultId, ct);
        }

        // 6. Điền chữ của mẫu, dựng ảnh PNG, mã hóa và lưu, rồi ghi tham chiếu vào hồ sơ
        await FillTemplateAsync(data, document, ct);
        byte[] image = renderer.Render(data);
        StoredContent stored = await IdentityImageStorage.SaveAsync(contentStore, image, ct);
        submission.PaperStorageKey = stored.StorageKey;
        submission.PaperSizeBytes = stored.SizeBytes;
        submission.PaperEncryptionKeyId = stored.EncryptionKeyId;
        submission.PaperWrappedDataKey = stored.WrappedDataKey;
        return stored.StorageKey;
    }

    /// <summary>
    /// Điền các đoạn chữ của mẫu vào dữ liệu dựng: biên bản đã tạo dùng đúng mẫu ghi trên biên bản (TemplateVersion), luồng cũ không có
    /// biên bản thì dùng mẫu đang áp dụng. Không có dịch vụ mẫu thì để trống để bộ dựng dùng chữ cố định.
    /// </summary>
    private async Task FillTemplateAsync(HandoverPaperData data, HandoverDocument? document, CancellationToken ct)
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
        if (data.IsAcceptance)
        {
            data.Paragraphs = template.Content.Accept;
        }
        else
        {
            data.Paragraphs = template.Content.Decline;
        }
    }

    /// <summary>Ngày mất, ngày đăng ký khai tử và nơi đăng ký in lên văn bản; không có hồ sơ hoặc chưa đủ dữ liệu thì để trống.</summary>
    private async Task FillDeathDatesAsync(HandoverPaperData data, Guid vaultId, CancellationToken ct)
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
