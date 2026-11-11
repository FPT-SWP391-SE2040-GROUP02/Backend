using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IHandoverDocumentService
{
    /// <summary>
    /// Người nhận chọn Chấp nhận hoặc Từ chối để bắt đầu biên bản. Chỉ khi lượt ở bước xác minh danh tính.
    /// Phần 1 (thông tin về Chủ kho) được chép luôn từ hồ sơ chứng tử đã duyệt, biên bản vào thẳng bước người nhận.
    /// Gọi lại cùng lựa chọn thì trả biên bản cũ; người nhận chưa ký thì được đổi lựa chọn.
    /// Lỗi: 404 lượt không phải của mình; 409 WRONG_STATE, DOCUMENT_STARTED.
    /// </summary>
    Task<HandoverDocumentResponse> CreateAsync(Guid turnId, CreateHandoverDocumentRequest request, CancellationToken ct);

    /// <summary>Biên bản của lượt mình đã nhận; null khi chưa bắt đầu.</summary>
    Task<HandoverDocumentResponse?> GetByTurnAsync(Guid turnId, CancellationToken ct);

    /// <summary>Mọi biên bản của người nhận, mới nhất trước (mục Biên Bản). Không giới hạn thời gian.</summary>
    Task<List<HandoverDocumentResponse>> ListMineAsync(CancellationToken ct);

    /// <summary>Biên bản của các kho mà mình là Người thực thi đang hiệu lực, mới nhất trước (chỉ để xem lại).</summary>
    Task<List<HandoverDocumentResponse>> ListForExecutorAsync(CancellationToken ct);

    /// <summary>Một biên bản của kho mình làm Người thực thi; kho khác hoặc id lạ cùng một lỗi 404.</summary>
    Task<HandoverDocumentResponse> GetForExecutorAsync(Guid documentId, CancellationToken ct);

    /// <summary>Biên bản của các kho mà Người xét duyệt này đã duyệt hồ sơ chứng tử, mới nhất trước.</summary>
    Task<List<HandoverDocumentResponse>> ListForVerifierAsync(CancellationToken ct);
}

public sealed class HandoverDocumentService : IHandoverDocumentService
{
    private readonly ICurrentUser currentUser;
    private readonly IHandoverRepository handover;
    private readonly IHandoverDocumentRepository documents;
    private readonly IVaultRepository vaults;
    private readonly IUserRepository users;
    private readonly IExecutorReader executorReader;
    private readonly IDeathCaseRepository deathCases;
    private readonly IDeathCaseDataProtector deathCaseProtector;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IAdministrativeUnitCatalog administrativeUnits;
    private readonly IHandoverDocumentNotifier? notifier;
    private readonly IOwnerIdentityReader? ownerIdentities;
    private readonly IBeneficiaryRepository? beneficiaries;
    private readonly IBeneficiaryDataProtector? beneficiaryProtector;
    private readonly IHandoverTemplateService? templates;

    public HandoverDocumentService(
        ICurrentUser currentUser,
        IHandoverRepository handover,
        IHandoverDocumentRepository documents,
        IVaultRepository vaults,
        IUserRepository users,
        IExecutorReader executorReader,
        IDeathCaseRepository deathCases,
        IDeathCaseDataProtector deathCaseProtector,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IAdministrativeUnitCatalog administrativeUnits,
        IHandoverDocumentNotifier? notifier = null,
        IOwnerIdentityReader? ownerIdentities = null,
        IBeneficiaryRepository? beneficiaries = null,
        IBeneficiaryDataProtector? beneficiaryProtector = null,
        IHandoverTemplateService? templates = null)
    {
        this.templates = templates;
        this.currentUser = currentUser;
        this.handover = handover;
        this.documents = documents;
        this.vaults = vaults;
        this.users = users;
        this.executorReader = executorReader;
        this.deathCases = deathCases;
        this.deathCaseProtector = deathCaseProtector;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.administrativeUnits = administrativeUnits;
        this.notifier = notifier;
        this.ownerIdentities = ownerIdentities;
        this.beneficiaries = beneficiaries;
        this.beneficiaryProtector = beneficiaryProtector;
    }

    public async Task<HandoverDocumentResponse> CreateAsync(Guid turnId, CreateHandoverDocumentRequest request, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await FindMyTurnAsync(turnId, userId, ct);
        if (turn.Status != HandoverTurnStatus.IdentityInProgress)
        {
            throw NotAllowed("WRONG_STATE", "Lượt này không ở bước lập biên bản.");
        }

        HandoverSubmissionKind kind = HandoverDocumentRules.ParseKind(request.Kind);
        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverDocument? existing = await documents.FindByTurnIdAsync(turn.Id, ct);
        if (existing != null)
        {
            if (existing.Kind != kind)
            {
                if (!HandoverDocumentRules.CanChangeKind(existing))
                {
                    throw NotAllowed("DOCUMENT_STARTED", "Biên bản đã sang bước sau nên không đổi lựa chọn được.");
                }
                existing.Kind = kind;
                existing.UpdatedAt = now;
                await unitOfWork.SaveChangesAsync(ct);
            }
            return await ToResponseAsync(existing, false, ct);
        }

        HandoverDocument document = new HandoverDocument();
        document.TurnId = turn.Id;
        document.Turn = turn;
        document.VaultId = turn.VaultId;
        document.Kind = kind;
        document.Status = HandoverDocumentStatus.WaitRecipient;
        document.ItemCount = turn.Items.Count;
        document.TemplateVersion = await ActiveTemplateKeyAsync(ct);
        document.CreatedAt = now;
        await FillOwnerPartAsync(document, turn.VaultId, ct);
        // Điền sẵn tên tài khoản cho người nhận; Phần 2 sẽ ghi lại họ tên người nhận tự khai.
        User? account = await users.FindByIdAsync(userId, ct);
        if (account != null)
        {
            document.RecipientFullName = account.FullName;
        }
        documents.Add(document);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (handover.IsUniqueViolation(ex))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Dữ liệu vừa được sửa ở nơi khác. Vui lòng tải lại rồi thử lại.");
        }

        return await ToResponseAsync(document, false, ct);
    }

    public async Task<HandoverDocumentResponse?> GetByTurnAsync(Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await FindMyTurnAsync(turnId, userId, ct);
        HandoverDocument? document = await documents.FindByTurnIdAsync(turn.Id, ct);
        if (document == null)
        {
            return null;
        }
        return await ToResponseAsync(document, false, ct);
    }

    public async Task<List<HandoverDocumentResponse>> ListMineAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        List<HandoverDocument> found = await documents.ListByRecipientAsync(userId, ct);
        return await ToResponsesAsync(found, false, ct);
    }

    public async Task<List<HandoverDocumentResponse>> ListForExecutorAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        List<ExecutorVaultRow> rows = await deathCases.FindExecutorVaultsAsync(userId, ct);
        List<Guid> vaultIds = new List<Guid>();
        foreach (ExecutorVaultRow row in rows)
        {
            vaultIds.Add(row.VaultId);
        }
        if (vaultIds.Count == 0)
        {
            return new List<HandoverDocumentResponse>();
        }
        List<HandoverDocument> found = await documents.ListByVaultIdsAsync(vaultIds, ct);
        return await ToResponsesAsync(found, false, ct);
    }

    public async Task<HandoverDocumentResponse> GetForExecutorAsync(Guid documentId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverDocument document = await FindForExecutorAsync(documentId, userId, ct);
        return await ToResponseAsync(document, false, ct);
    }

    public async Task<List<HandoverDocumentResponse>> ListForVerifierAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Status != UserStatus.Active || user.Role != UserRole.Verifier)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.RoleNotAllowed,
                "Chỉ Người xét duyệt mới xem được danh sách này.");
        }
        List<HandoverDocument> found = await documents.ListForVerifierAsync(userId, ct);
        List<HandoverDocumentResponse> responses = await ToResponsesAsync(found, true, ct);

        // Thêm số liệu để màn Người xét duyệt xếp theo thời điểm nộp và gom theo kho.
        List<Guid> vaultIds = new List<Guid>();
        List<Guid> turnIds = new List<Guid>();
        foreach (HandoverDocument document in found)
        {
            if (!vaultIds.Contains(document.VaultId))
            {
                vaultIds.Add(document.VaultId);
            }
            turnIds.Add(document.TurnId);
        }
        VerifierDocumentContext context = await documents.LoadVerifierContextAsync(userId, vaultIds, turnIds, ct);
        for (int i = 0; i < responses.Count; i++)
        {
            FillVerifierContext(responses[i], found[i], context);
        }
        return responses;
    }

    /// <summary>Điền các trường chỉ Người xét duyệt thấy: thời điểm nộp, nộp lại, ngày duyệt hồ sơ báo tin, tổng người nhận của kho.</summary>
    private static void FillVerifierContext(
        HandoverDocumentResponse response, HandoverDocument document, VerifierDocumentContext context)
    {
        if (document.SubmissionId != null)
        {
            DateTimeOffset submittedAt;
            if (context.SubmittedAtBySubmission.TryGetValue(document.SubmissionId.Value, out submittedAt))
            {
                response.SubmittedAt = submittedAt;
            }
        }

        int submissionCount;
        if (context.SubmissionCountByTurn.TryGetValue(document.TurnId, out submissionCount))
        {
            response.Resubmitted = submissionCount > 1;
        }

        DateTimeOffset approvedAt;
        if (context.CaseApprovedAtByVault.TryGetValue(document.VaultId, out approvedAt))
        {
            response.CaseApprovedAt = approvedAt;
        }

        int recipientTotal;
        if (context.RecipientTotalByVault.TryGetValue(document.VaultId, out recipientTotal))
        {
            response.VaultRecipientTotal = recipientTotal;
        }
    }

    /// <summary>Dựng phản hồi cho nhiều biên bản, tra tên mỗi người một lần.</summary>
    private async Task<List<HandoverDocumentResponse>> ToResponsesAsync(
        List<HandoverDocument> found, bool forVerifier, CancellationToken ct)
    {
        Dictionary<Guid, string> cache = new Dictionary<Guid, string>();
        List<HandoverDocumentResponse> list = new List<HandoverDocumentResponse>();
        foreach (HandoverDocument document in found)
        {
            list.Add(await ToResponseAsync(document, forVerifier, ct, cache));
        }
        return list;
    }

    /// <summary>Khóa mẫu đang dùng cho biên bản mới; không có dịch vụ mẫu thì dùng mẫu gốc.</summary>
    private async Task<string> ActiveTemplateKeyAsync(CancellationToken ct)
    {
        if (templates == null)
        {
            return HandoverDocumentRules.TemplateVersion;
        }
        return await templates.ActiveVersionKeyAsync(ct);
    }

    private async Task<ResolvedHandoverTemplate> ResolveTemplateAsync(string versionKey, CancellationToken ct)
    {
        if (templates == null)
        {
            ResolvedHandoverTemplate fallback = new ResolvedHandoverTemplate();
            fallback.VersionKey = HandoverDocumentRules.TemplateVersion;
            fallback.Content = HandoverTemplateDefaults.Create();
            return fallback;
        }
        return await templates.ResolveAsync(versionKey, ct);
    }

    private async Task<HandoverDocumentResponse> ToResponseAsync(
        HandoverDocument document, bool forVerifier, CancellationToken ct, Dictionary<Guid, string>? cache = null)
    {
        Dictionary<Guid, string> names = cache ?? new Dictionary<Guid, string>();
        HandoverDocumentResponse response = new HandoverDocumentResponse();
        response.Id = document.Id;
        response.TurnId = document.TurnId;
        response.VaultId = document.VaultId;
        response.Code = HandoverDocumentRules.Code(document.Id);
        response.Kind = document.Kind == HandoverSubmissionKind.Accept ? "ACCEPT" : "DECLINE";
        response.Status = HandoverDocumentRules.StatusToApi(document.Status);
        response.TemplateVersion = document.TemplateVersion;
        ResolvedHandoverTemplate template = await ResolveTemplateAsync(document.TemplateVersion, ct);
        response.DeclineNotice = template.Content.DeclineNotice;
        if (document.Kind == HandoverSubmissionKind.Accept)
        {
            response.TemplateParagraphs = template.Content.Accept;
        }
        else
        {
            response.TemplateParagraphs = template.Content.Decline;
        }
        response.ContainerName = document.Turn.ContainerName;
        response.ItemCount = document.ItemCount;
        response.SupplementReason = document.SupplementReason;
        response.CreatedAt = document.CreatedAt;
        response.CompletedAt = document.CompletedAt;
        if (forVerifier)
        {
            response.SubmissionId = document.SubmissionId;
        }

        response.Owner.FullName = document.OwnerFullName;
        response.Owner.DateOfDeath = document.DateOfDeath;
        response.Owner.CertificateNumber = document.CertificateNumber;
        response.Owner.CertificateIssuedOn = document.CertificateIssuedOn;
        response.Owner.DeathRegistrationPlace = document.DeathRegistrationPlace;
        response.Owner.ResidenceAddress = document.OwnerResidenceAddress;

        response.Recipient.FullName = document.RecipientFullName;
        response.Recipient.BirthYear = document.RecipientBirthYear;
        response.Recipient.CitizenIdLast4 = document.RecipientCitizenIdLast4;
        response.Recipient.CitizenId = await FindRecipientCitizenIdAsync(document, ct);
        response.Recipient.ResidenceAddress = document.RecipientResidenceAddress;
        response.Recipient.SignedAt = document.RecipientSignedAt;

        response.Verifier.SignedByName = await NameOfAsync(document.VerifierUserId, names, ct);
        response.Verifier.Checks = HandoverDocumentRules.DecodeChecks(document.VerifierChecks);
        response.Verifier.SignedAt = document.VerifierSignedAt;
        return response;
    }

    /// <summary>
    /// Chép Phần 1 từ phiên bản hồ sơ chứng tử mà Người xét duyệt đã duyệt (ReviewVersionNo), bản này đã khóa nên không đổi nữa.
    /// Hồ sơ chứng tử chỉ bắt buộc có tệp giấy chứng tử: ngày mất, số giấy, ngày cấp thiếu thì để trống, không chặn người nhận.
    /// Họ tên Chủ kho lấy theo hồ sơ (khớp giấy); hồ sơ không ghi thì lấy tên tài khoản Chủ kho.
    /// </summary>
    private async Task FillOwnerPartAsync(HandoverDocument document, Guid vaultId, CancellationToken ct)
    {
        string accountName = string.Empty;
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault != null)
        {
            User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
            if (owner != null)
            {
                accountName = owner.FullName;
            }
            document.OwnerResidenceAddress = await FindOwnerAddressAsync(vault.OwnerId, ct);
        }
        document.OwnerFullName = Cut(accountName, 200);

        DeathCase? deathCase = await deathCases.FindOpenByVaultIdAsync(vaultId, ct);
        if (deathCase == null || deathCase.ReviewVersionNo == null)
        {
            return;
        }
        DeathCaseVersion version = DeathCaseReviewRules.FindReviewVersion(deathCase);
        document.DateOfDeath = version.DateOfDeath;
        document.CertificateIssuedOn = version.IssuedOn;
        // D8: chụp nơi đăng ký khai tử dạng câu từ danh mục, để biên bản xem lại được kể cả khi hồ sơ bị dọn.
        document.DeathRegistrationPlace = Cut(
            administrativeUnits.BuildRegistrationPlace(version.DeathRegistrationProvinceCode, version.DeathRegistrationWardCode), 300);
        DeathCaseIdentityData? identity = deathCaseProtector.Unprotect(version);
        if (identity == null)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(identity.OwnerFullName))
        {
            document.OwnerFullName = Cut(identity.OwnerFullName, 200);
        }
        if (!string.IsNullOrWhiteSpace(identity.CertificateNumber))
        {
            document.CertificateNumber = Cut(identity.CertificateNumber, 100);
        }
    }

    /// <summary>
    /// Nơi thường trú của Chủ kho theo danh tính đã xác minh bằng eKYC. Chưa có danh tính hoặc không giải mã được
    /// (thiếu KEK, dữ liệu hỏng) thì để trống thay vì chặn người nhận lập biên bản: biên bản in dòng chấm.
    /// </summary>
    private async Task<string> FindOwnerAddressAsync(Guid ownerId, CancellationToken ct)
    {
        if (ownerIdentities == null)
        {
            return string.Empty;
        }
        try
        {
            return HandoverDocumentRules.CleanAddress(await ownerIdentities.FindBoundResidenceAddressAsync(ownerId, ct));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return string.Empty;
        }
    }

    private async Task<string> FindRecipientCitizenIdAsync(HandoverDocument document, CancellationToken ct)
    {
        if (document.RecipientSignedAt == null && string.IsNullOrWhiteSpace(document.RecipientCitizenIdLast4))
        {
            return string.Empty;
        }
        if (beneficiaries == null || beneficiaryProtector == null)
        {
            return document.RecipientCitizenIdLast4;
        }
        try
        {
            Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(document.VaultId, document.Turn.BeneficiaryId, ct);
            if (beneficiary == null)
            {
                return document.RecipientCitizenIdLast4;
            }
            BeneficiaryPersonalData personal = beneficiaryProtector.Unprotect(beneficiary);
            return !string.IsNullOrWhiteSpace(personal.CitizenId) ? personal.CitizenId : document.RecipientCitizenIdLast4;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return document.RecipientCitizenIdLast4;
        }
    }

    private static string Cut(string text, int maxLength)
    {
        string trimmed = text.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }
        return trimmed.Substring(0, maxLength);
    }

    private async Task<string> NameOfAsync(Guid? userId, Dictionary<Guid, string> names, CancellationToken ct)
    {
        if (userId == null)
        {
            return string.Empty;
        }
        if (names.ContainsKey(userId.Value))
        {
            return names[userId.Value];
        }
        User? user = await users.FindByIdAsync(userId.Value, ct);
        string name = user == null ? string.Empty : user.FullName;
        names[userId.Value] = name;
        return name;
    }

    /// <summary>Lượt của người đang đăng nhập; id lạ và lượt của người khác cho cùng một lỗi 404 (SEC-04).</summary>
    private async Task<HandoverTurn> FindMyTurnAsync(Guid turnId, Guid userId, CancellationToken ct)
    {
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.ClaimantUserId != userId)
        {
            throw NotFound();
        }
        return turn;
    }

    private async Task<HandoverDocument> FindForExecutorAsync(Guid documentId, Guid userId, CancellationToken ct)
    {
        HandoverDocument? document = await documents.FindByIdAsync(documentId, ct);
        if (document == null || !await IsActiveExecutorAsync(document.VaultId, userId, ct))
        {
            throw NotFound();
        }
        return document;
    }

    private async Task<bool> IsActiveExecutorAsync(Guid vaultId, Guid userId, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault == null || vault.OwnerId == userId)
        {
            return false;
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vaultId, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                return true;
            }
        }
        return false;
    }

    private static AppException NotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy biên bản.");
    }

    private static AppException NotAllowed(string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}
