using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

// Biên bản bàn giao, các nút theo thứ tự luồng:
//   1. Người thực thi bấm Mở bàn giao ......... OpenAsync
//   2. Luật sư lập biên bản ................... UseOwnerSplitAsync, SetLinesAsync
//   3. Luật sư chốt biên bản .................. FinalizeAsync (tạo lượt, gửi thư mời)
//   4. Luật sư xác nhận danh tính ............. HandoverSubmissionReviewService.ApproveAsync → AdvanceAfterIdentityAsync
//   5. Ba bên ký ............................... SignAsVerifierAsync, SignAsExecutorAsync, SignAsRecipientAsync
//   6. Đủ chữ ký: mở quyền tải cho người nhận (CompleteIfAllSigned), Người thực thi gửi mã nhận
// Luật sư mở lại được ở bước 2 hoặc 3 (ReopenAsync): chữ ký cũ mất hiệu lực, phải ký lại từ đầu.
/// <summary>Biên bản bàn giao: hệ thống chỉ làm theo biên bản luật sư lập, không tự chia tài sản.</summary>
public sealed class HandoverRecordService : IHandoverRecordService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverRecordRepository records;
    private readonly IHandoverRepository handover;
    private readonly IHandoverSubmissionRepository submissions;
    private readonly IBeneficiaryDataProtector protector;
    private readonly HandoverRecordTurnSync turnSync;
    private readonly ExecutorKeyIssuer keyIssuer;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IAssetContentStore contentStore;
    private readonly IHandoverRequestNoticeSender noticeSender;
    private readonly IExecutorInvitationRepository invitations;

    public HandoverRecordService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IExecutorReader executorReader,
        IHandoverRecordRepository records,
        IHandoverRepository handover,
        IHandoverSubmissionRepository submissions,
        IBeneficiaryDataProtector protector,
        HandoverRecordTurnSync turnSync,
        ExecutorKeyIssuer keyIssuer,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IAssetContentStore contentStore,
        IHandoverRequestNoticeSender noticeSender,
        IExecutorInvitationRepository invitations)
    {
        this.invitations = invitations;
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.executorReader = executorReader;
        this.records = records;
        this.handover = handover;
        this.submissions = submissions;
        this.protector = protector;
        this.turnSync = turnSync;
        this.keyIssuer = keyIssuer;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.contentStore = contentStore;
        this.noticeSender = noticeSender;
    }

    // ---------------- Người thực thi ----------------

    public async Task<List<ExecutorVaultResponse>> ListMyVaultsAsync(CancellationToken ct)
    {
        // 1. Các kho tôi đã nhận lời làm Người thực thi (Chủ kho không bao giờ là Người thực thi của kho mình)
        Guid userId = currentUser.RequireUserId();
        List<Executor> executors = await records.ListAcceptedExecutorsAsync(userId, ct);

        // 2. Một dòng cho mỗi kho, kèm biên bản nếu đã mở bàn giao
        List<ExecutorVaultResponse> result = new List<ExecutorVaultResponse>();
        foreach (Executor executor in executors)
        {
            Vault vault = executor.Vault;
            if (vault.OwnerId == userId)
            {
                continue;
            }
            ExecutorVaultResponse item = new ExecutorVaultResponse
            {
                VaultId = vault.Id,
                OwnerFullName = vault.Owner.FullName,
                PlanStatus = vault.Plan.Status.ToApiString(),
                ExecutorNote = vault.ExecutorNote,
                ExecutorNoteUpdatedAt = vault.ExecutorNoteUpdatedAt,
                AcceptedAt = executor.AcceptedAt
            };
            HandoverRecord? record = await records.FindByVaultIdAsync(vault.Id, ct);
            if (record != null)
            {
                item.HandoverRecordId = record.Id;
                item.HandoverRecordStatus = record.Status.ToApiString();
            }
            result.Add(item);
        }
        return result;
    }

    public async Task<HandoverRecordResponse?> GetForExecutorAsync(Guid vaultId, CancellationToken ct)
    {
        // 1. Người gọi phải là Người thực thi đang phụ trách kho
        Guid userId = currentUser.RequireUserId();
        Vault vault = await RequireExecutorVaultAsync(vaultId, userId, ct);

        // 2. Chưa mở bàn giao thì chưa có biên bản
        HandoverRecord? record = await records.FindByVaultIdAsync(vault.Id, ct);
        if (record == null)
        {
            return null;
        }

        // 3. Dựng biên bản; ký được khi đang ở bước ký và chưa ký bản này
        HandoverRecordResponse response = await BuildResponseAsync(record, vault, null, ct);
        response.CanSign = record.Status == HandoverRecordStatus.Signing
            && HandoverRecordRules.FindSignature(record, HandoverSignerRole.Executor, null) == null;
        return response;
    }

    // ---------------- Người nhận gửi yêu cầu nhận tài sản ----------------

    public async Task<HandoverRequestPreviewResponse> PreviewRequestAsync(string token, CancellationToken ct)
    {
        // 1. Link phải đúng; không cần đăng nhập, chỉ trả tên và email đã che
        Beneficiary beneficiary = await RequireBeneficiaryByTokenAsync(token, ct);
        Vault vault = await RequireVaultAsync(beneficiary.VaultId, ct);
        BeneficiaryPersonalData data = protector.Unprotect(beneficiary);

        // 2. Gửi được khi kế hoạch đang hoạt động, hoặc đang có biên bản chưa đủ chữ ký
        HandoverRecord? record = await records.FindByVaultIdAsync(vault.Id, ct);
        bool canRequest = vault.Plan.Status == PlanStatus.Active && record == null;
        if (record != null && record.Status != HandoverRecordStatus.Completed)
        {
            canRequest = true;
        }
        HandoverRequestPreviewResponse response = new HandoverRequestPreviewResponse
        {
            OwnerName = await FindUserNameAsync(vault.OwnerId, ct),
            RecipientName = data.FullName,
            CanRequest = canRequest
        };
        if (!string.IsNullOrWhiteSpace(data.Email))
        {
            response.InvitedEmailMasked = MaskEmail(NormalizeEmail(data.Email));
        }
        return response;
    }

    public async Task<MyHandoverClaimResponse> SubmitClaimAsync(
        string token, string? note, List<UploadedFile> files, CancellationToken ct)
    {
        // 1. Người gửi phải đăng nhập đúng email Chủ kho đã khai cho người nhận này
        User user = await RequireVerifiedUserAsync(ct);
        Beneficiary beneficiary = await RequireBeneficiaryByTokenAsync(token, ct);
        Vault vault = await RequireVaultAsync(beneficiary.VaultId, ct);
        EnsureEmailMatches(user, beneficiary);
        if (vault.OwnerId == user.Id)
        {
            throw HandoverAccess.NotAllowed("OWNER_CANNOT_CLAIM", "Chủ kho không tự gửi yêu cầu nhận tài sản của kho mình.");
        }
        string cleanNote = NormalizeNote(note);
        HandoverRecordFileStorage.Validate(files, true);

        // 2. Chưa có biên bản thì tạo và phân công luật sư (kho phải đang hoạt động); có rồi thì gắn vào
        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverRecord? record = await records.FindByVaultIdAsync(vault.Id, ct);
        bool created = false;
        if (record == null)
        {
            if (vault.Plan.Status != PlanStatus.Active)
            {
                throw HandoverAccess.NotAllowed("PLAN_NOT_ACTIVE",
                    "Kho này chưa kích hoạt kế hoạch bàn giao nên chưa nhận yêu cầu.");
            }
            Guid? verifierId = await PickVerifierAsync(vault, user.Id, ct);
            if (verifierId == null)
            {
                throw HandoverAccess.NotAllowed("NO_VERIFIER",
                    "Hiện chưa có luật sư nào nhận được hồ sơ này. Vui lòng thử lại sau.");
            }
            record = new HandoverRecord
            {
                VaultId = vault.Id,
                PlanId = vault.Plan.Id,
                Status = HandoverRecordStatus.Drafting,
                RevisionNo = 1,
                RequestedByUserId = user.Id,
                VerifierUserId = verifierId.Value,
                RequestedAt = now,
                UpdatedAt = now
            };
            records.Add(record);
            // Khóa cấu hình kho trong lúc luật sư xét.
            vault.Plan.Status = PlanStatus.HandoverRequested;
            vault.Plan.UpdatedAt = now;
            created = true;
        }
        else
        {
            if (record.Status == HandoverRecordStatus.Completed)
            {
                throw HandoverAccess.NotAllowed("RECORD_COMPLETED", "Biên bản bàn giao của kho này đã đủ chữ ký.");
            }
            if (FindClaim(record, beneficiary.Id) != null)
            {
                throw HandoverAccess.NotAllowed("ALREADY_REQUESTED",
                    "Bạn đã gửi yêu cầu cho kho này. Xem kết quả ở mục Yêu cầu của tôi.");
            }
            record.UpdatedAt = now;
        }

        // 3. Ghi yêu cầu và giấy tờ (mã hóa), lưu một lần; lưu lỗi thì xóa tệp đã ghi
        HandoverClaim claim = new HandoverClaim
        {
            RecordId = record.Id,
            BeneficiaryId = beneficiary.Id,
            UserId = user.Id,
            Status = HandoverClaimStatus.Submitted,
            Note = cleanNote,
            CreatedAt = now,
            UpdatedAt = now
        };
        records.AddClaim(claim);
        // EF tự gắn yêu cầu vào biên bản đang theo dõi; chỉ gắn tay khi chưa có (biên bản mới tạo).
        if (!record.Claims.Contains(claim))
        {
            record.Claims.Add(claim);
        }
        List<HandoverRecordFile> saved = await HandoverRecordFileStorage.SaveAllAsync(
            contentStore, files, record.Id, claim.Id, HandoverRecordFileKind.ClaimDocument, user.Id, now, ct);
        AttachFiles(record, saved);
        await SaveOrDeleteFilesAsync(saved, ct);

        // 4. Báo sau khi lưu: lần đầu báo Chủ kho (hủy được), Người thực thi và luật sư; lần sau chỉ báo luật sư
        string requesterName = protector.Unprotect(beneficiary).FullName;
        await NotifyRequestAsync(vault, record, requesterName, created, ct);

        MyHandoverClaimResponse response = await BuildMyClaimResponseAsync(claim, record, vault, ct);
        return response;
    }

    public async Task<List<MyHandoverClaimResponse>> ListMyClaimsAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        List<HandoverClaim> claims = await records.ListClaimsByUserAsync(userId, ct);
        List<MyHandoverClaimResponse> result = new List<MyHandoverClaimResponse>();
        foreach (HandoverClaim item in claims)
        {
            HandoverRecord? record = await records.FindByIdAsync(item.RecordId, ct);
            if (record == null)
            {
                continue;
            }
            Vault vault = await RequireVaultAsync(record.VaultId, ct);
            HandoverClaim? claim = FindClaimById(record, item.Id);
            if (claim == null)
            {
                continue;
            }
            result.Add(await BuildMyClaimResponseAsync(claim, record, vault, ct));
        }
        return result;
    }

    public async Task<MyHandoverClaimResponse> SupplementClaimAsync(
        Guid claimId, string? note, List<UploadedFile> files, CancellationToken ct)
    {
        // 1. Yêu cầu phải của người đang đăng nhập và đang được luật sư yêu cầu bổ sung
        Guid userId = currentUser.RequireUserId();
        (HandoverRecord record, HandoverClaim claim) = await RequireMyClaimAsync(claimId, userId, ct);
        if (claim.Status != HandoverClaimStatus.SupplementRequested)
        {
            throw HandoverAccess.NotAllowed("NOT_SUPPLEMENT_REQUESTED", "Luật sư chưa yêu cầu bổ sung giấy tờ cho yêu cầu này.");
        }
        HandoverRecordFileStorage.Validate(files, true);
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Thêm tệp, quay về chờ luật sư xét, lưu
        DateTimeOffset now = timeProvider.GetUtcNow();
        string cleanNote = NormalizeNote(note);
        if (cleanNote.Length > 0)
        {
            claim.Note = cleanNote;
        }
        claim.Status = HandoverClaimStatus.Submitted;
        claim.UpdatedAt = now;
        List<HandoverRecordFile> saved = await HandoverRecordFileStorage.SaveAllAsync(
            contentStore, files, record.Id, claim.Id, HandoverRecordFileKind.ClaimDocument, userId, now, ct);
        AttachFiles(record, saved);
        await SaveOrDeleteFilesAsync(saved, ct);

        MyHandoverClaimResponse response = await BuildMyClaimResponseAsync(claim, record, vault, ct);
        return response;
    }

    public async Task<PreparedDownload> PrepareMyClaimFileAsync(Guid claimId, Guid fileId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        (HandoverRecord record, HandoverClaim claim) = await RequireMyClaimAsync(claimId, userId, ct);
        HandoverRecordFile? file = FindFile(record, fileId);
        if (file == null || file.ClaimId != claim.Id)
        {
            throw HandoverAccess.NotFound();
        }
        return await HandoverRecordFileStorage.PrepareAsync(contentStore, file, ct);
    }

    public async Task<HandoverRecordResponse> SignAsExecutorAsync(Guid vaultId, CancellationToken ct)
    {
        // 1. Người gọi phải là Người thực thi đang phụ trách kho, kho phải có biên bản
        Guid userId = currentUser.RequireUserId();
        Vault vault = await RequireExecutorVaultAsync(vaultId, userId, ct);
        HandoverRecord? record = await records.FindByVaultIdAsync(vault.Id, ct);
        if (record == null)
        {
            throw RecordNotFound();
        }

        // 2. Ký (bấm lặp thì giữ chữ ký cũ), đủ chữ ký thì hoàn tất, lưu
        DateTimeOffset now = timeProvider.GetUtcNow();
        AddSignature(record, HandoverSignerRole.Executor, userId, null, now);
        await CompleteIfAllSignedAsync(record, vault, now, ct);
        await SaveAsync(ct);

        HandoverRecordResponse response = await BuildResponseAsync(record, vault, null, ct);
        return response;
    }

    // ---------------- Luật sư ----------------

    public async Task<List<HandoverRecordSummaryResponse>> ListForVerifierAsync(CancellationToken ct)
    {
        // 1. Phải là luật sư đang hoạt động
        Guid userId = await RequireActiveVerifierAsync(ct);

        // 2. Một dòng cho mỗi biên bản được giao
        List<HandoverRecord> list = await records.ListByVerifierAsync(userId, ct);
        List<HandoverRecordSummaryResponse> result = new List<HandoverRecordSummaryResponse>();
        foreach (HandoverRecord record in list)
        {
            Vault? vault = await vaults.FindByIdAsync(record.VaultId, ct);
            if (vault == null)
            {
                continue;
            }
            HandoverRecordSummaryResponse summary = new HandoverRecordSummaryResponse
            {
                Id = record.Id,
                VaultId = vault.Id,
                VaultName = vault.Name,
                OwnerName = await FindUserNameAsync(vault.OwnerId, ct),
                Status = record.Status.ToApiString(),
                RecipientCount = HandoverRecordRules.GroupAssetsByRecipient(record.Lines).Count,
                RequestedAt = record.RequestedAt
            };
            result.Add(summary);
        }
        return result;
    }

    public async Task<HandoverRecordResponse> GetForVerifierAsync(Guid recordId, CancellationToken ct)
    {
        // 1. Biên bản phải được giao cho luật sư này
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Dựng biên bản kèm dữ liệu để lập
        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    public async Task<HandoverRecordResponse> UseOwnerSplitAsync(Guid recordId, CancellationToken ct)
    {
        // 1. Biên bản của luật sư này và đang lập
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        RequireStatus(record, HandoverRecordStatus.Drafting, "Chỉ lấy biên bản phân chia khi biên bản đang lập.");
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Đọc biên bản phân chia: mỗi người nhận của gói nhận mọi tài sản trong gói (gói nhiều người là đồng thừa kế)
        List<Designation> designations = await handover.FindStartableDesignationsAsync(vault.Id, ct);
        List<(Guid BeneficiaryId, Guid AssetId)> lines = new List<(Guid BeneficiaryId, Guid AssetId)>();
        HashSet<(Guid, Guid)> usedPairs = new HashSet<(Guid, Guid)>();
        HashSet<Guid> rejected = RejectedBeneficiaries(record);
        foreach (Designation designation in designations)
        {
            if (rejected.Contains(designation.BeneficiaryId))
            {
                // Người bị luật sư từ chối không được ghi vào biên bản.
                continue;
            }
            List<ContainerAsset> links = designation.Container.Assets.OrderBy(a => a.AddedAt).ThenBy(a => a.Id).ToList();
            foreach (ContainerAsset link in links)
            {
                if (link.Asset.ContentDeletedAt != null || usedPairs.Contains((designation.BeneficiaryId, link.AssetId)))
                {
                    continue;
                }
                usedPairs.Add((designation.BeneficiaryId, link.AssetId));
                lines.Add((designation.BeneficiaryId, link.AssetId));
            }
        }
        if (lines.Count == 0)
        {
            throw HandoverAccess.NotAllowed("NO_OWNER_SPLIT",
                "Chủ kho chưa lập biên bản phân chia. Vui lòng tự lập biên bản.");
        }

        // 3. Thay toàn bộ dòng, lưu
        DateTimeOffset now = timeProvider.GetUtcNow();
        ReplaceLines(record, lines, now);
        await SaveAsync(ct);

        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    public async Task<HandoverRecordResponse> SetLinesAsync(
        Guid recordId, SetHandoverRecordLinesRequest request, CancellationToken ct)
    {
        // 1. Biên bản của luật sư này và đang lập
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        RequireStatus(record, HandoverRecordStatus.Drafting, "Biên bản đã chốt. Mở lại biên bản để sửa.");
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Kiểm dòng: không trùng cặp người nhận - tài sản, người nhận và tài sản phải thuộc kho
        List<(Guid BeneficiaryId, Guid AssetId)> lines = new List<(Guid BeneficiaryId, Guid AssetId)>();
        foreach (HandoverRecordLineRequest line in request.Lines)
        {
            lines.Add((line.BeneficiaryId, line.AssetId));
        }
        string? error = await ValidateLinesAsync(vault.Id, record, lines, ct);
        if (error != null)
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError, error);
        }

        // 3. Thay toàn bộ dòng, lưu
        DateTimeOffset now = timeProvider.GetUtcNow();
        ReplaceLines(record, lines, now);
        await SaveAsync(ct);

        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    public async Task<HandoverRecordResponse> FinalizeAsync(Guid recordId, CancellationToken ct)
    {
        // 1. Biên bản của luật sư này, đang lập và có dòng hợp lệ
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        RequireStatus(record, HandoverRecordStatus.Drafting, "Biên bản đã chốt.");
        Vault vault = await RequireVaultAsync(record.VaultId, ct);
        List<(Guid BeneficiaryId, Guid AssetId)> lines = new List<(Guid BeneficiaryId, Guid AssetId)>();
        foreach (HandoverRecordLine line in record.Lines)
        {
            lines.Add((line.BeneficiaryId, line.AssetId));
        }
        string? error = await ValidateLinesAsync(vault.Id, record, lines, ct);
        if (error != null)
        {
            throw HandoverAccess.NotAllowed("RECORD_INVALID", error);
        }
        string? claimError = HandoverRecordRules.ValidateClaimsForFinalize(record.Claims);
        if (claimError != null)
        {
            throw HandoverAccess.NotAllowed("CLAIMS_PENDING", claimError);
        }

        // 2. Tạo hoặc cập nhật lượt cho từng người nhận; kho sang giai đoạn bàn giao
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<HandoverInvitation> invitations = await turnSync.SyncAsync(record, now, ct);
        record.Status = HandoverRecordStatus.IdentityCheck;
        record.FinalizedAt = now;
        record.UpdatedAt = now;
        vault.Plan.Status = PlanStatus.HandoverInProgress;
        vault.Plan.UpdatedAt = now;

        // 3. Ai cũng đã được xác nhận danh tính (chốt lại sau khi sửa) thì sang bước ký luôn
        await AdvanceIfIdentitiesConfirmedAsync(record, ct);

        // 4. Người thực thi giữ khóa mở gói: đảm bảo kho có khóa đọc được, lưu một lần
        await keyIssuer.EnsureReadableAsync(vault, AuditActorRole.System, null, now, ct);
        await SaveAsync(ct);

        // 5. Gửi thư mời sau khi lưu, rồi lưu kết quả gửi
        if (invitations.Count > 0)
        {
            await turnSync.SendInvitationsAsync(invitations, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    public async Task<HandoverRecordResponse> ReopenAsync(Guid recordId, CancellationToken ct)
    {
        // 1. Biên bản của luật sư này, đã chốt nhưng chưa hoàn tất
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        if (record.Status != HandoverRecordStatus.IdentityCheck && record.Status != HandoverRecordStatus.Signing)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", "Chỉ mở lại được biên bản đã chốt và chưa hoàn tất.");
        }
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Quay về bước 1, tăng số bản: mọi chữ ký cũ mất hiệu lực
        DateTimeOffset now = timeProvider.GetUtcNow();
        record.Status = HandoverRecordStatus.Drafting;
        record.RevisionNo = record.RevisionNo + 1;
        record.FinalizedAt = null;
        record.UpdatedAt = now;
        await SaveAsync(ct);

        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    public async Task<HandoverRecordResponse> ReviewClaimAsync(
        Guid recordId, Guid claimId, ReviewHandoverClaimRequest request, CancellationToken ct)
    {
        // 1. Biên bản của luật sư này và đang lập; yêu cầu phải thuộc biên bản
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        RequireStatus(record, HandoverRecordStatus.Drafting, "Chỉ xét giấy tờ khi biên bản đang lập. Mở lại biên bản để xét.");
        HandoverClaim? claim = FindClaimById(record, claimId);
        if (claim == null)
        {
            throw HandoverAccess.NotFound();
        }
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Ghi kết quả: bổ sung và từ chối phải có lý do; từ chối thì bỏ người này khỏi biên bản
        string decision = request.Decision.Trim().ToUpperInvariant();
        string reason = string.Empty;
        if (request.Reason != null)
        {
            reason = request.Reason.Trim();
        }
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (decision == "ACCEPT")
        {
            claim.Status = HandoverClaimStatus.Accepted;
            claim.ReviewReason = null;
        }
        else if (decision == "REQUEST_SUPPLEMENT" || decision == "REJECT")
        {
            if (reason.Length == 0)
            {
                throw ValidationErrors.For("reason", "Vui lòng ghi lý do để người nhận biết.");
            }
            claim.ReviewReason = reason;
            if (decision == "REJECT")
            {
                claim.Status = HandoverClaimStatus.Rejected;
                RemoveLinesOf(record, claim.BeneficiaryId);
            }
            else
            {
                claim.Status = HandoverClaimStatus.SupplementRequested;
            }
        }
        else
        {
            throw ValidationErrors.For("decision", "Kết quả xét phải là ACCEPT, REQUEST_SUPPLEMENT hoặc REJECT.");
        }
        claim.ReviewedByUserId = userId;
        claim.ReviewedAt = now;
        claim.UpdatedAt = now;
        record.UpdatedAt = now;
        await SaveAsync(ct);

        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    public async Task<HandoverRecordResponse> AddRecipientAsync(
        Guid recordId, AddHandoverRecipientRequest request, CancellationToken ct)
    {
        // 1. Biên bản của luật sư này và đang lập
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        RequireStatus(record, HandoverRecordStatus.Drafting, "Chỉ thêm người nhận khi biên bản đang lập.");
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Kiểm thông tin: đủ CCCD, họ tên, ngày sinh, email; không trùng Chủ kho hay người nhận đã có
        BeneficiaryPersonalData data = new BeneficiaryPersonalData();
        data.CitizenId = CitizenIdRules.NormalizeCitizenId(request.CitizenId, "citizenId");
        data.FullName = request.FullName.Trim();
        if (data.FullName.Length == 0)
        {
            throw ValidationErrors.For("fullName", "Thiếu họ tên.");
        }
        if (request.DateOfBirth == null)
        {
            throw ValidationErrors.For("dateOfBirth", "Thiếu ngày sinh.");
        }
        data.DateOfBirth = request.DateOfBirth.Value;
        RecipientRules.EnsureAdultBirthDate(data.DateOfBirth, RecipientRules.TodayInVietnam(timeProvider));
        data.Email = NormalizeEmail(request.Email);
        if (!data.Email.Contains('@'))
        {
            throw ValidationErrors.For("email", "Email không hợp lệ.");
        }
        if (request.Phone != null && request.Phone.Trim().Length > 0)
        {
            data.Phone = request.Phone.Trim();
        }
        Beneficiary beneficiary = new Beneficiary();
        beneficiary.VaultId = vault.Id;
        protector.Protect(beneficiary, data);
        List<Beneficiary> vaultBeneficiaries = await records.ListVaultBeneficiariesAsync(vault.Id, ct);
        foreach (Beneficiary existing in vaultBeneficiaries)
        {
            if (existing.CitizenIdHash != null && beneficiary.CitizenIdHash != null
                && existing.CitizenIdHash.SequenceEqual(beneficiary.CitizenIdHash))
            {
                throw ValidationErrors.For("citizenId", "Người này đã có trong kho.");
            }
        }
        // Email không trùng Chủ kho, nhân sự (Người xét duyệt, Người thực thi), Người thực thi của kho hay người nhận khác.
        await RecipientRules.EnsureEmailAvailableAsync(
            data.Email, vault, null, users, invitations, protector, vaultBeneficiaries, ct);

        // 3. Lưu hồ sơ người nhận mới; luật sư ghi tài sản cho người này ở biên bản
        DateTimeOffset now = timeProvider.GetUtcNow();
        beneficiary.CreatedAt = now;
        records.AddBeneficiary(beneficiary);
        record.UpdatedAt = now;
        await SaveAsync(ct);

        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    public async Task<HandoverRecordResponse> AddEvidenceAsync(
        Guid recordId, List<UploadedFile> files, CancellationToken ct)
    {
        // 1. Biên bản của luật sư này và đang lập
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        RequireStatus(record, HandoverRecordStatus.Drafting, "Chỉ đính kèm tệp căn cứ khi biên bản đang lập.");
        HandoverRecordFileStorage.Validate(files, true);
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Lưu tệp (mã hóa), lưu lỗi thì xóa tệp đã ghi
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<HandoverRecordFile> saved = await HandoverRecordFileStorage.SaveAllAsync(
            contentStore, files, record.Id, null, HandoverRecordFileKind.Evidence, userId, now, ct);
        AttachFiles(record, saved);
        record.UpdatedAt = now;
        await SaveOrDeleteFilesAsync(saved, ct);

        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    public async Task<HandoverRecordResponse> RemoveEvidenceAsync(Guid recordId, Guid fileId, CancellationToken ct)
    {
        // 1. Biên bản của luật sư này, đang lập; chỉ xóa được tệp căn cứ của luật sư
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        RequireStatus(record, HandoverRecordStatus.Drafting, "Chỉ gỡ tệp căn cứ khi biên bản đang lập.");
        HandoverRecordFile? file = FindFile(record, fileId);
        if (file == null || file.Kind != HandoverRecordFileKind.Evidence)
        {
            throw HandoverAccess.NotFound();
        }
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Xóa bản ghi rồi mới xóa nội dung
        DateTimeOffset now = timeProvider.GetUtcNow();
        record.Files.Remove(file);
        records.RemoveFile(file);
        record.UpdatedAt = now;
        await SaveAsync(ct);
        List<HandoverRecordFile> removed = new List<HandoverRecordFile>();
        removed.Add(file);
        await HandoverRecordFileStorage.DeleteAllAsync(contentStore, removed);

        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    public async Task<PreparedDownload> PrepareVerifierFileAsync(Guid recordId, Guid fileId, CancellationToken ct)
    {
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        HandoverRecordFile? file = FindFile(record, fileId);
        if (file == null)
        {
            throw HandoverAccess.NotFound();
        }
        return await HandoverRecordFileStorage.PrepareAsync(contentStore, file, ct);
    }

    // ---------------- Chủ kho ----------------

    public async Task<OwnerHandoverRequestResponse?> GetOwnerRequestAsync(Guid vaultId, CancellationToken ct)
    {
        Vault vault = await RequireOwnerVaultAsync(vaultId, ct);
        HandoverRecord? record = await records.FindByVaultIdAsync(vault.Id, ct);
        if (record == null)
        {
            return null;
        }
        OwnerHandoverRequestResponse response = new OwnerHandoverRequestResponse
        {
            RecordId = record.Id,
            Status = record.Status.ToApiString(),
            RequestedAt = record.RequestedAt,
            CanCancel = record.Status != HandoverRecordStatus.Completed
        };
        List<Beneficiary> beneficiaries = await records.ListVaultBeneficiariesAsync(vault.Id, ct);
        foreach (HandoverClaim claim in record.Claims.OrderBy(c => c.CreatedAt))
        {
            foreach (Beneficiary beneficiary in beneficiaries)
            {
                if (beneficiary.Id == claim.BeneficiaryId)
                {
                    response.RequesterNames.Add(ReadFullName(beneficiary));
                }
            }
        }
        return response;
    }

    public async Task CancelByOwnerAsync(Guid vaultId, CancellationToken ct)
    {
        // 1. Chủ kho của kho; phải đang có yêu cầu và biên bản chưa đủ chữ ký
        Vault vault = await RequireOwnerVaultAsync(vaultId, ct);
        HandoverRecord? record = await records.FindByVaultIdAsync(vault.Id, ct);
        if (record == null)
        {
            throw HandoverAccess.NotAllowed("NO_REQUEST", "Kho không có yêu cầu nhận tài sản nào đang mở.");
        }
        if (record.Status == HandoverRecordStatus.Completed)
        {
            throw HandoverAccess.NotAllowed("RECORD_COMPLETED", "Biên bản đã đủ chữ ký nên không hủy được nữa.");
        }

        // 2. Hủy biên bản và mọi lượt đang chạy; kho mở khóa, kế hoạch về Đang hoạt động
        DateTimeOffset now = timeProvider.GetUtcNow();
        record.Status = HandoverRecordStatus.Cancelled;
        record.CancelledAt = now;
        record.UpdatedAt = now;
        List<HandoverTurn> turns = await records.FindTurnsWithItemsAsync(record.Id, ct);
        foreach (HandoverTurn turn in turns)
        {
            if (!HandoverRules.IsTerminal(turn.Status))
            {
                turn.Status = HandoverTurnStatus.Cancelled;
                turn.EndedAt = now;
                turn.UpdatedAt = now;
            }
        }
        vault.Plan.Status = PlanStatus.Active;
        vault.Plan.UpdatedAt = now;
        await SaveAsync(ct);
    }

    public async Task<HandoverRecordResponse> SignAsVerifierAsync(Guid recordId, CancellationToken ct)
    {
        // 1. Biên bản của luật sư này
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverRecord record = await RequireVerifierRecordAsync(recordId, userId, ct);
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Ký, đủ chữ ký thì hoàn tất, lưu
        DateTimeOffset now = timeProvider.GetUtcNow();
        AddSignature(record, HandoverSignerRole.Verifier, userId, null, now);
        await CompleteIfAllSignedAsync(record, vault, now, ct);
        await SaveAsync(ct);

        HandoverRecordResponse response = await BuildVerifierResponseAsync(record, vault, ct);
        return response;
    }

    // ---------------- Người nhận ----------------

    public async Task<HandoverRecordResponse> GetForRecipientAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải thuộc người đang đăng nhập và đi theo một biên bản
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await HandoverAccess.FindMyTurnAsync(handover, turnId, userId, ct);
        HandoverRecord record = await RequireRecordOfTurnAsync(turn, ct);
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Chỉ trả phần của người nhận này
        HandoverRecordResponse response = await BuildResponseAsync(record, vault, turn.BeneficiaryId, ct);
        response.CanSign = record.Status == HandoverRecordStatus.Signing
            && HandoverRecordRules.FindSignature(record, HandoverSignerRole.Recipient, turn.BeneficiaryId) == null;
        return response;
    }

    public async Task<HandoverRecordResponse> SignAsRecipientAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải thuộc người đang đăng nhập, người này phải có tên trong biên bản
        Guid userId = currentUser.RequireUserId();
        HandoverTurn turn = await HandoverAccess.FindMyTurnAsync(handover, turnId, userId, ct);
        HandoverRecord record = await RequireRecordOfTurnAsync(turn, ct);
        Vault vault = await RequireVaultAsync(record.VaultId, ct);
        if (!HandoverRecordRules.GroupAssetsByRecipient(record.Lines).ContainsKey(turn.BeneficiaryId))
        {
            throw HandoverAccess.NotFound();
        }

        // 2. Ký, đủ chữ ký thì hoàn tất, lưu
        DateTimeOffset now = timeProvider.GetUtcNow();
        AddSignature(record, HandoverSignerRole.Recipient, userId, turn.BeneficiaryId, now);
        await CompleteIfAllSignedAsync(record, vault, now, ct);
        await SaveAsync(ct);

        HandoverRecordResponse response = await BuildResponseAsync(record, vault, turn.BeneficiaryId, ct);
        return response;
    }

    // ---------------- Dùng nội bộ ----------------

    public async Task AdvanceAfterIdentityAsync(HandoverTurn turn, DateTimeOffset now, CancellationToken ct)
    {
        if (turn.RecordId == null)
        {
            return;
        }
        HandoverRecord? record = await records.FindByIdAsync(turn.RecordId.Value, ct);
        if (record == null)
        {
            return;
        }
        // Lượt theo biên bản không có hạn quyết định: người nhận chỉ còn việc ký biên bản.
        turn.DecisionDeadline = null;
        await AdvanceIfIdentitiesConfirmedAsync(record, ct);
        if (record.Status == HandoverRecordStatus.Signing)
        {
            record.UpdatedAt = now;
        }
    }

    public async Task<Guid?> FindVerifierUserIdAsync(Guid vaultId, CancellationToken ct)
    {
        HandoverRecord? record = await records.FindByVaultIdAsync(vaultId, ct);
        if (record == null)
        {
            return null;
        }
        return record.VerifierUserId;
    }

    // ---------------- Các bước dùng chung ----------------

    /// <summary>Đang ở bước 2 mà mọi người nhận đã được xác nhận danh tính thì mở bước 3.</summary>
    private async Task AdvanceIfIdentitiesConfirmedAsync(HandoverRecord record, CancellationToken ct)
    {
        if (record.Status != HandoverRecordStatus.IdentityCheck)
        {
            return;
        }
        Dictionary<Guid, HandoverTurn> turns = await turnSync.FindActiveTurnsAsync(record, ct);
        if (HandoverRecordRules.AllIdentitiesConfirmed(record, turns))
        {
            record.Status = HandoverRecordStatus.Signing;
            await NotifySigningAsync(record, ct);
        }
    }

    /// <summary>
    /// Báo người nhận và Người thực thi của kho rằng biên bản đã tới bước ký (luật sư xác nhận danh tính xong).
    /// Gửi lỗi thì bỏ qua từng thư: bước ký đã mở, thư không chặn luồng.
    /// </summary>
    private async Task NotifySigningAsync(HandoverRecord record, CancellationToken ct)
    {
        try
        {
            Vault? vault = await vaults.FindByIdAsync(record.VaultId, ct);
            if (vault == null)
            {
                return;
            }
            string ownerName = await FindUserNameAsync(vault.OwnerId, ct);
            List<(string Email, string FullName, string Audience)> targets = new List<(string, string, string)>();

            HashSet<Guid> recipientIds = new HashSet<Guid>();
            foreach (HandoverRecordLine line in record.Lines)
            {
                recipientIds.Add(line.BeneficiaryId);
            }
            foreach (Beneficiary beneficiary in await records.ListVaultBeneficiariesAsync(record.VaultId, ct))
            {
                if (!recipientIds.Contains(beneficiary.Id))
                {
                    continue;
                }
                try
                {
                    BeneficiaryPersonalData personal = protector.Unprotect(beneficiary);
                    if (!string.IsNullOrWhiteSpace(personal.Email))
                    {
                        targets.Add((personal.Email, personal.FullName, HandoverRequestNotice.Recipient));
                    }
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    // Hồ sơ không giải mã được: bỏ qua người này.
                }
            }
            foreach (Executor executor in await executorReader.GetActiveAsync(record.VaultId, ct))
            {
                if (executor.UserId == null)
                {
                    continue;
                }
                User? account = await users.FindByIdAsync(executor.UserId.Value, ct);
                if (account != null && !string.IsNullOrWhiteSpace(account.Email))
                {
                    targets.Add((account.Email, account.FullName, HandoverRequestNotice.Executor));
                }
            }

            foreach ((string email, string fullName, string audience) in targets)
            {
                try
                {
                    HandoverRequestNotice notice = new HandoverRequestNotice
                    {
                        ToEmail = email,
                        FullName = fullName,
                        Audience = audience,
                        OwnerName = ownerName,
                        Stage = HandoverRequestNotice.StageSigning
                    };
                    await noticeSender.SendAsync(notice, ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Một thư lỗi không chặn các thư còn lại.
                }
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Không ném lại: bước ký đã mở.
        }
    }

    /// <summary>
    /// Thêm chữ ký của bản hiện tại. Chỉ ký được ở bước 3; ký lặp thì giữ chữ ký cũ.
    /// </summary>
    private void AddSignature(
        HandoverRecord record, HandoverSignerRole role, Guid signerUserId, Guid? beneficiaryId, DateTimeOffset now)
    {
        if (record.Status != HandoverRecordStatus.Signing)
        {
            if (record.Status == HandoverRecordStatus.Completed)
            {
                throw HandoverAccess.NotAllowed("RECORD_COMPLETED", "Biên bản đã đủ chữ ký.");
            }
            throw HandoverAccess.NotAllowed("NOT_SIGNING_STEP",
                "Chưa tới bước ký: biên bản phải được chốt và mọi người nhận phải được xác nhận danh tính.");
        }
        if (HandoverRecordRules.FindSignature(record, role, beneficiaryId) != null)
        {
            return;
        }
        HandoverRecordSignature signature = new HandoverRecordSignature
        {
            RecordId = record.Id,
            RevisionNo = record.RevisionNo,
            Role = role,
            SignerUserId = signerUserId,
            BeneficiaryId = beneficiaryId,
            SignedAt = now
        };
        record.Signatures.Add(signature);
        records.AddSignature(signature);
        record.UpdatedAt = now;
    }

    /// <summary>Đủ chữ ký ba bên thì biên bản hoàn tất: mỗi người nhận được mở quyền tải tài sản của mình.</summary>
    private async Task CompleteIfAllSignedAsync(HandoverRecord record, Vault vault, DateTimeOffset now, CancellationToken ct)
    {
        if (record.Status != HandoverRecordStatus.Signing || !HandoverRecordRules.AllSigned(record))
        {
            return;
        }
        Dictionary<Guid, HandoverTurn> turns = await turnSync.FindActiveTurnsAsync(record, ct);
        foreach (HandoverTurn turn in turns.Values)
        {
            if (turn.Grant != null)
            {
                // Một lần ký khác vừa hoàn tất biên bản và mở quyền tải cho lượt này (ví dụ người nhận bấm ký hai lần
                // cùng lúc). Lượt đã đọc trước đó đã cũ, nên dừng thay vì tạo thêm quyền tải.
                throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                    "Biên bản vừa được hoàn tất ở nơi khác. Vui lòng tải lại.");
            }
            if (turn.Status != HandoverTurnStatus.ReadyToAccept || turn.ClaimantUserId == null)
            {
                continue;
            }
            HandoverGrant grant = HandoverRules.CommitAccept(turn, turn.ClaimantUserId.Value, now);
            handover.AddGrant(grant);
        }
        record.Status = HandoverRecordStatus.Completed;
        record.CompletedAt = now;
        record.UpdatedAt = now;
        vault.Plan.UpdatedAt = now;
        // Kế hoạch vẫn ở giai đoạn bàn giao để Người thực thi gửi mã và người nhận tải; HandoverRules đóng khi mọi lượt xong.
        List<HandoverTurn> allTurns = await handover.FindTurnsByPlanAsync(vault.Plan.Id, ct);
        HandoverRules.CloseIfAllTerminal(vault.Plan, allTurns, now);
    }

    private void ReplaceLines(HandoverRecord record, List<(Guid BeneficiaryId, Guid AssetId)> lines, DateTimeOffset now)
    {
        List<HandoverRecordLine> old = new List<HandoverRecordLine>(record.Lines);
        foreach (HandoverRecordLine line in old)
        {
            record.Lines.Remove(line);
            records.RemoveLine(line);
        }
        int offset = 0;
        foreach ((Guid beneficiaryId, Guid assetId) in lines)
        {
            HandoverRecordLine line = new HandoverRecordLine
            {
                RecordId = record.Id,
                BeneficiaryId = beneficiaryId,
                AssetId = assetId,
                // Cộng dần từng tích tắc để giữ đúng thứ tự dòng luật sư gửi.
                CreatedAt = now.AddTicks(offset)
            };
            record.Lines.Add(line);
            records.AddLine(line);
            offset++;
        }
        record.UpdatedAt = now;
    }

    private async Task<string?> ValidateLinesAsync(
        Guid vaultId, HandoverRecord record, List<(Guid BeneficiaryId, Guid AssetId)> lines, CancellationToken ct)
    {
        HashSet<Guid> beneficiaryIds = new HashSet<Guid>();
        foreach (Beneficiary beneficiary in await records.ListVaultBeneficiariesAsync(vaultId, ct))
        {
            beneficiaryIds.Add(beneficiary.Id);
        }
        HashSet<Guid> assetIds = new HashSet<Guid>();
        foreach (Asset asset in await records.ListVaultAssetsAsync(vaultId, ct))
        {
            assetIds.Add(asset.Id);
        }
        HashSet<Guid> rejected = RejectedBeneficiaries(record);
        foreach ((Guid beneficiaryId, Guid _) in lines)
        {
            if (rejected.Contains(beneficiaryId))
            {
                return "Có người nhận đã bị từ chối giấy tờ: không ghi tài sản cho người này.";
            }
        }
        return HandoverRecordRules.ValidateLines(lines, beneficiaryIds, assetIds);
    }

    private async Task<Guid?> PickVerifierAsync(Vault vault, Guid requesterUserId, CancellationToken ct)
    {
        List<Guid> excluded = new List<Guid>();
        excluded.Add(vault.OwnerId);
        excluded.Add(requesterUserId);
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId != null)
            {
                excluded.Add(executor.UserId.Value);
            }
        }
        // Luật sư không được trùng người nhận của kho (so theo email).
        List<string> excludedEmails = new List<string>();
        foreach (Beneficiary beneficiary in await records.ListVaultBeneficiariesAsync(vault.Id, ct))
        {
            BeneficiaryPersonalData data = protector.Unprotect(beneficiary);
            if (!string.IsNullOrWhiteSpace(data.Email))
            {
                string email = data.Email.Trim().ToLowerInvariant();
                if (!excludedEmails.Contains(email))
                {
                    excludedEmails.Add(email);
                }
            }
        }
        return await records.FindVerifierCandidateAsync(excluded, excludedEmails, ct);
    }

    private async Task<HandoverRecordResponse> BuildVerifierResponseAsync(
        HandoverRecord record, Vault vault, CancellationToken ct)
    {
        HandoverRecordResponse response = await BuildResponseAsync(record, vault, null, ct);
        response.CanSign = record.Status == HandoverRecordStatus.Signing
            && HandoverRecordRules.FindSignature(record, HandoverSignerRole.Verifier, null) == null;

        foreach (Asset asset in await records.ListVaultAssetsAsync(vault.Id, ct))
        {
            HandoverRecordAssetResponse item = new HandoverRecordAssetResponse
            {
                AssetId = asset.Id,
                Name = asset.Name,
                Kind = asset.Kind.ToApiString()
            };
            response.AvailableAssets.Add(item);
        }
        foreach (Beneficiary beneficiary in await records.ListVaultBeneficiariesAsync(vault.Id, ct))
        {
            HandoverRecordPersonResponse person = new HandoverRecordPersonResponse
            {
                BeneficiaryId = beneficiary.Id,
                FullName = ReadFullName(beneficiary),
                CitizenId = ReadCitizenId(beneficiary)
            };
            response.AvailableRecipients.Add(person);
        }

        // Biên bản phân chia của Chủ kho: gom chỉ định theo gói.
        List<Designation> designations = await handover.FindStartableDesignationsAsync(vault.Id, ct);
        Dictionary<Guid, OwnerSplitPackageResponse> packages = new Dictionary<Guid, OwnerSplitPackageResponse>();
        foreach (Designation designation in designations)
        {
            if (!packages.ContainsKey(designation.ContainerId))
            {
                OwnerSplitPackageResponse package = new OwnerSplitPackageResponse
                {
                    PackageName = designation.Container.Name
                };
                foreach (ContainerAsset link in designation.Container.Assets.OrderBy(a => a.AddedAt))
                {
                    if (link.Asset.ContentDeletedAt == null)
                    {
                        package.AssetIds.Add(link.AssetId);
                    }
                }
                packages[designation.ContainerId] = package;
                response.OwnerSplit.Add(package);
            }
            packages[designation.ContainerId].BeneficiaryIds.Add(designation.BeneficiaryId);
        }

        // Yêu cầu nhận tài sản kèm giấy tờ, và tệp căn cứ: chỉ luật sư thấy.
        Dictionary<Guid, Beneficiary> beneficiaryById = new Dictionary<Guid, Beneficiary>();
        foreach (Beneficiary beneficiary in await records.ListVaultBeneficiariesAsync(vault.Id, ct))
        {
            beneficiaryById[beneficiary.Id] = beneficiary;
        }
        foreach (HandoverClaim claim in record.Claims.OrderBy(c => c.CreatedAt))
        {
            HandoverClaimResponse item = new HandoverClaimResponse
            {
                Id = claim.Id,
                BeneficiaryId = claim.BeneficiaryId,
                Status = claim.Status.ToApiString(),
                Note = claim.Note,
                ReviewReason = claim.ReviewReason,
                CreatedAt = claim.CreatedAt,
                ReviewedAt = claim.ReviewedAt
            };
            if (beneficiaryById.ContainsKey(claim.BeneficiaryId))
            {
                item.FullName = ReadFullName(beneficiaryById[claim.BeneficiaryId]);
                item.CitizenId = ReadCitizenId(beneficiaryById[claim.BeneficiaryId]);
            }
            foreach (HandoverRecordFile file in record.Files.OrderBy(f => f.CreatedAt))
            {
                if (file.ClaimId == claim.Id)
                {
                    item.Files.Add(ToFileResponse(file));
                }
            }
            response.Claims.Add(item);
        }
        foreach (HandoverRecordFile file in record.Files.OrderBy(f => f.CreatedAt))
        {
            if (file.Kind == HandoverRecordFileKind.Evidence)
            {
                response.EvidenceFiles.Add(ToFileResponse(file));
            }
        }
        return response;
    }

    /// <summary>
    /// Dựng biên bản. onlyBeneficiaryId khác null thì chỉ trả phần của người nhận đó (màn người nhận).
    /// </summary>
    private async Task<HandoverRecordResponse> BuildResponseAsync(
        HandoverRecord record, Vault vault, Guid? onlyBeneficiaryId, CancellationToken ct)
    {
        HandoverRecordResponse response = new HandoverRecordResponse
        {
            Id = record.Id,
            VaultId = vault.Id,
            VaultName = vault.Name,
            OwnerName = await FindUserNameAsync(vault.OwnerId, ct),
            Status = record.Status.ToApiString(),
            RevisionNo = record.RevisionNo,
            RequestedAt = record.RequestedAt,
            FinalizedAt = record.FinalizedAt,
            CompletedAt = record.CompletedAt,
            VerifierName = await FindUserNameAsync(record.VerifierUserId, ct),
            ExecutorName = await FindExecutorNameAsync(vault.Id, ct)
        };
        HandoverRecordSignature? verifierSignature = HandoverRecordRules.FindSignature(record, HandoverSignerRole.Verifier, null);
        if (verifierSignature != null)
        {
            response.VerifierSignedAt = verifierSignature.SignedAt;
        }
        HandoverRecordSignature? executorSignature = HandoverRecordRules.FindSignature(record, HandoverSignerRole.Executor, null);
        if (executorSignature != null)
        {
            response.ExecutorSignedAt = executorSignature.SignedAt;
        }

        // Tên tài sản lấy theo kho hiện tại; tài sản đã bị xóa nội dung vẫn hiện tên rỗng thay vì làm hỏng màn hình.
        Dictionary<Guid, Asset> assetsById = new Dictionary<Guid, Asset>();
        foreach (Asset asset in await records.ListVaultAssetsAsync(vault.Id, ct))
        {
            assetsById[asset.Id] = asset;
        }
        Dictionary<Guid, Beneficiary> beneficiariesById = new Dictionary<Guid, Beneficiary>();
        foreach (Beneficiary beneficiary in await records.ListVaultBeneficiariesAsync(vault.Id, ct))
        {
            beneficiariesById[beneficiary.Id] = beneficiary;
        }
        Dictionary<Guid, HandoverTurn> turns = await turnSync.FindActiveTurnsAsync(record, ct);
        List<Guid> turnIds = new List<Guid>();
        foreach (HandoverTurn turn in turns.Values)
        {
            turnIds.Add(turn.Id);
        }
        Dictionary<Guid, HandoverSubmission> latestSubmissionByTurn = new Dictionary<Guid, HandoverSubmission>();
        if (turnIds.Count > 0)
        {
            List<HandoverSubmission> all = await submissions.ListByTurnIdsAsync(turnIds, ct);
            foreach (HandoverSubmission submission in all.OrderBy(s => s.CreatedAt))
            {
                if (submission.Status == HandoverSubmissionStatus.Superseded)
                {
                    continue;
                }
                latestSubmissionByTurn[submission.TurnId] = submission;
            }
        }

        Dictionary<Guid, List<Guid>> grouped = HandoverRecordRules.GroupAssetsByRecipient(record.Lines);
        foreach (KeyValuePair<Guid, List<Guid>> pair in grouped)
        {
            if (onlyBeneficiaryId != null && pair.Key != onlyBeneficiaryId.Value)
            {
                continue;
            }
            HandoverRecordRecipientResponse recipient = new HandoverRecordRecipientResponse
            {
                BeneficiaryId = pair.Key
            };
            if (beneficiariesById.ContainsKey(pair.Key))
            {
                recipient.FullName = ReadFullName(beneficiariesById[pair.Key]);
                recipient.CitizenId = ReadCitizenId(beneficiariesById[pair.Key]);
            }
            foreach (Guid assetId in pair.Value)
            {
                HandoverRecordAssetResponse item = new HandoverRecordAssetResponse
                {
                    AssetId = assetId
                };
                if (assetsById.ContainsKey(assetId))
                {
                    item.Name = assetsById[assetId].Name;
                    item.Kind = assetsById[assetId].Kind.ToApiString();
                }
                recipient.Assets.Add(item);
            }
            if (turns.ContainsKey(pair.Key))
            {
                HandoverTurn turn = turns[pair.Key];
                recipient.TurnId = turn.Id;
                recipient.TurnStatus = turn.Status.ToApiString();
                recipient.Claimed = turn.ClaimantUserId != null;
                recipient.IdentityConfirmedAt = turn.IdentityConfirmedAt;
                if (latestSubmissionByTurn.ContainsKey(turn.Id))
                {
                    HandoverSubmission submission = latestSubmissionByTurn[turn.Id];
                    recipient.SubmissionId = submission.Id;
                    recipient.SubmissionStatus = submission.Status.ToApiString();
                }
            }
            HandoverRecordSignature? signature = HandoverRecordRules.FindSignature(record, HandoverSignerRole.Recipient, pair.Key);
            if (signature != null)
            {
                recipient.SignedAt = signature.SignedAt;
            }
            response.Recipients.Add(recipient);
        }
        return response;
    }

    private string ReadFullName(Beneficiary beneficiary)
    {
        try
        {
            return protector.Unprotect(beneficiary).FullName;
        }
        catch (Exception)
        {
            // Không giải mã được (thiếu khóa, dữ liệu hỏng) thì để trống thay vì làm hỏng cả màn hình.
            return string.Empty;
        }
    }

    private string ReadCitizenId(Beneficiary beneficiary)
    {
        try
        {
            return protector.Unprotect(beneficiary).CitizenId;
        }
        catch (Exception)
        {
            // Không giải mã được thì để trống, giống họ tên.
            return string.Empty;
        }
    }

    private async Task<string> FindUserNameAsync(Guid userId, CancellationToken ct)
    {
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null)
        {
            return string.Empty;
        }
        return user.FullName;
    }

    private async Task<Vault> RequireExecutorVaultAsync(Guid vaultId, Guid userId, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault == null || !await HandoverAccess.IsActiveExecutorAsync(executorReader, vault, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        return vault;
    }

    private async Task<Vault> RequireVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault == null)
        {
            throw RecordNotFound();
        }
        return vault;
    }

    private async Task<Guid> RequireActiveVerifierAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null)
        {
            throw new AppException(AppErrorType.Unauthorized, ErrorCode.SessionExpired,
                "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        }
        if (user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive,
                "Tài khoản đang bị khóa hoặc vô hiệu hóa.");
        }
        if (user.Role != UserRole.Verifier)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.RoleNotAllowed,
                "Chỉ luật sư mới làm được biên bản bàn giao.");
        }
        return userId;
    }

    /// <summary>Biên bản phải được giao cho luật sư này; không thì 404 như không tồn tại.</summary>
    private async Task<HandoverRecord> RequireVerifierRecordAsync(Guid recordId, Guid userId, CancellationToken ct)
    {
        HandoverRecord? record = await records.FindByIdAsync(recordId, ct);
        if (record == null || record.VerifierUserId != userId)
        {
            throw RecordNotFound();
        }
        return record;
    }

    private async Task<HandoverRecord> RequireRecordOfTurnAsync(HandoverTurn turn, CancellationToken ct)
    {
        if (turn.RecordId == null)
        {
            throw HandoverAccess.NotFound();
        }
        HandoverRecord? record = await records.FindByIdAsync(turn.RecordId.Value, ct);
        if (record == null)
        {
            throw HandoverAccess.NotFound();
        }
        return record;
    }

    private static void RequireStatus(HandoverRecord record, HandoverRecordStatus status, string message)
    {
        if (record.Status != status)
        {
            throw HandoverAccess.NotAllowed("WRONG_STATE", message);
        }
    }

    private async Task<string> FindExecutorNameAsync(Guid vaultId, CancellationToken ct)
    {
        List<Executor> executors = await executorReader.GetActiveAsync(vaultId, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId != null)
            {
                return await FindUserNameAsync(executor.UserId.Value, ct);
            }
        }
        return string.Empty;
    }

    private async Task<Vault> RequireOwnerVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault == null || vault.OwnerId != userId)
        {
            throw HandoverAccess.NotFound();
        }
        return vault;
    }

    private async Task<User> RequireVerifiedUserAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null)
        {
            throw HandoverAccess.NotFound();
        }
        if (user.EmailVerifiedAt == null)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.EmailNotVerified,
                "Vui lòng xác minh email trước khi gửi yêu cầu nhận tài sản.");
        }
        return user;
    }

    private async Task<Beneficiary> RequireBeneficiaryByTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw InvalidRequestLink();
        }
        string hash = HandoverRecordRules.HashRequestToken(token.Trim());
        Beneficiary? beneficiary = await records.FindBeneficiaryByRequestTokenHashAsync(hash, ct);
        if (beneficiary == null)
        {
            throw InvalidRequestLink();
        }
        return beneficiary;
    }

    private static AppException InvalidRequestLink()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverInvitationInvalid,
            "Link gửi yêu cầu không đúng hoặc đã được thay bằng link mới.");
    }

    /// <summary>
    /// Email tài khoản phải trùng email Chủ kho đã khai cho người nhận. Sai thì 403 HANDOVER_EMAIL_MISMATCH kèm email đã che.
    /// </summary>
    private void EnsureEmailMatches(User user, Beneficiary beneficiary)
    {
        BeneficiaryPersonalData data = protector.Unprotect(beneficiary);
        string accountEmail = NormalizeEmail(user.Email);
        if (!string.IsNullOrWhiteSpace(data.Email) && NormalizeEmail(data.Email) == accountEmail)
        {
            return;
        }
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(data.Email))
        {
            extensions["invitedEmailMasked"] = MaskEmail(NormalizeEmail(data.Email));
        }
        throw new AppException(AppErrorType.Forbidden, ErrorCode.HandoverEmailMismatch,
            "Link này dành cho một email khác. Hãy đăng nhập bằng đúng email Chủ kho đã khai.", extensions);
    }

    private async Task<(HandoverRecord Record, HandoverClaim Claim)> RequireMyClaimAsync(
        Guid claimId, Guid userId, CancellationToken ct)
    {
        List<HandoverClaim> mine = await records.ListClaimsByUserAsync(userId, ct);
        foreach (HandoverClaim item in mine)
        {
            if (item.Id == claimId)
            {
                HandoverRecord? record = await records.FindByIdAsync(item.RecordId, ct);
                if (record != null)
                {
                    HandoverClaim? claim = FindClaimById(record, claimId);
                    if (claim != null)
                    {
                        return (record, claim);
                    }
                }
            }
        }
        throw HandoverAccess.NotFound();
    }

    private async Task<MyHandoverClaimResponse> BuildMyClaimResponseAsync(
        HandoverClaim claim, HandoverRecord record, Vault vault, CancellationToken ct)
    {
        MyHandoverClaimResponse response = new MyHandoverClaimResponse
        {
            Id = claim.Id,
            RecordId = record.Id,
            OwnerName = await FindUserNameAsync(vault.OwnerId, ct),
            Status = claim.Status.ToApiString(),
            RecordStatus = record.Status.ToApiString(),
            Note = claim.Note,
            ReviewReason = claim.ReviewReason,
            CreatedAt = claim.CreatedAt,
            ReviewedAt = claim.ReviewedAt
        };
        foreach (HandoverRecordFile file in record.Files.OrderBy(f => f.CreatedAt))
        {
            if (file.ClaimId == claim.Id)
            {
                response.Files.Add(ToFileResponse(file));
            }
        }
        return response;
    }

    private async Task NotifyRequestAsync(
        Vault vault, HandoverRecord record, string requesterName, bool firstRequest, CancellationToken ct)
    {
        string ownerName = await FindUserNameAsync(vault.OwnerId, ct);
        List<(Guid UserId, string Audience)> targets = new List<(Guid UserId, string Audience)>();
        targets.Add((record.VerifierUserId, HandoverRequestNotice.Verifier));
        if (firstRequest)
        {
            targets.Add((vault.OwnerId, HandoverRequestNotice.Owner));
            foreach (Executor executor in await executorReader.GetActiveAsync(vault.Id, ct))
            {
                if (executor.UserId != null)
                {
                    targets.Add((executor.UserId.Value, HandoverRequestNotice.Executor));
                }
            }
        }
        foreach ((Guid targetId, string audience) in targets)
        {
            try
            {
                User? target = await users.FindByIdAsync(targetId, ct);
                if (target == null || string.IsNullOrWhiteSpace(target.Email))
                {
                    continue;
                }
                HandoverRequestNotice notice = new HandoverRequestNotice
                {
                    ToEmail = target.Email,
                    FullName = target.FullName,
                    Audience = audience,
                    OwnerName = ownerName,
                    RequesterName = requesterName
                };
                await noticeSender.SendAsync(notice, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Không ném lại: yêu cầu đã lưu; một thư lỗi không chặn các thư còn lại.
            }
        }
    }

    private async Task SaveOrDeleteFilesAsync(List<HandoverRecordFile> saved, CancellationToken ct)
    {
        try
        {
            await SaveAsync(ct);
        }
        catch (Exception)
        {
            await HandoverRecordFileStorage.DeleteAllAsync(contentStore, saved);
            throw;
        }
    }

    private void AttachFiles(HandoverRecord record, List<HandoverRecordFile> files)
    {
        foreach (HandoverRecordFile file in files)
        {
            records.AddFile(file);
            if (!record.Files.Contains(file))
            {
                record.Files.Add(file);
            }
        }
    }

    private void RemoveLinesOf(HandoverRecord record, Guid beneficiaryId)
    {
        List<HandoverRecordLine> lines = new List<HandoverRecordLine>(record.Lines);
        foreach (HandoverRecordLine line in lines)
        {
            if (line.BeneficiaryId == beneficiaryId)
            {
                record.Lines.Remove(line);
                records.RemoveLine(line);
            }
        }
    }

    private static HashSet<Guid> RejectedBeneficiaries(HandoverRecord record)
    {
        HashSet<Guid> result = new HashSet<Guid>();
        foreach (HandoverClaim claim in record.Claims)
        {
            if (claim.Status == HandoverClaimStatus.Rejected)
            {
                result.Add(claim.BeneficiaryId);
            }
        }
        return result;
    }

    private static HandoverClaim? FindClaim(HandoverRecord record, Guid beneficiaryId)
    {
        foreach (HandoverClaim claim in record.Claims)
        {
            if (claim.BeneficiaryId == beneficiaryId)
            {
                return claim;
            }
        }
        return null;
    }

    private static HandoverClaim? FindClaimById(HandoverRecord record, Guid claimId)
    {
        foreach (HandoverClaim claim in record.Claims)
        {
            if (claim.Id == claimId)
            {
                return claim;
            }
        }
        return null;
    }

    private static HandoverRecordFile? FindFile(HandoverRecord record, Guid fileId)
    {
        foreach (HandoverRecordFile file in record.Files)
        {
            if (file.Id == fileId)
            {
                return file;
            }
        }
        return null;
    }

    private static HandoverRecordFileResponse ToFileResponse(HandoverRecordFile file)
    {
        HandoverRecordFileResponse response = new HandoverRecordFileResponse
        {
            Id = file.Id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            CreatedAt = file.CreatedAt
        };
        return response;
    }

    private static string NormalizeNote(string? note)
    {
        if (note == null)
        {
            return string.Empty;
        }
        string trimmed = note.Trim();
        if (trimmed.Length > 1000)
        {
            throw ValidationErrors.For("note", "Lời nhắn tối đa 1000 ký tự.");
        }
        return trimmed;
    }

    /// <summary>Email dùng để so sánh: bỏ khoảng trắng hai đầu và đổi sang chữ thường.</summary>
    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    /// <summary>Che email dạng m•••••h@example.com: giữ ký tự đầu và cuối của phần tên, giữ nguyên tên miền.</summary>
    private static string MaskEmail(string email)
    {
        int atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return "•••";
        }
        string localPart = email.Substring(0, atIndex);
        string domain = email.Substring(atIndex);
        if (localPart.Length <= 2)
        {
            return localPart.Substring(0, 1) + "•••" + domain;
        }
        string hidden = new string('•', localPart.Length - 2);
        string lastChar = localPart.Substring(localPart.Length - 1);
        return localPart.Substring(0, 1) + hidden + lastChar + domain;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (handover.IsUniqueViolation(ex) || IsConcurrencyConflict(ex))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Dữ liệu vừa được sửa ở nơi khác. Vui lòng tải lại rồi thử lại.");
        }
    }

    private static bool IsConcurrencyConflict(Exception ex)
    {
        return ex.GetType().Name == "DbUpdateConcurrencyException";
    }

    /// <summary>Không tìm thấy biên bản, hoặc người gọi không được xem.</summary>
    public static AppException RecordNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy biên bản bàn giao.");
    }
}
