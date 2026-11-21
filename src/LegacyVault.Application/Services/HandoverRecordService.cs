using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
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
        TimeProvider timeProvider)
    {
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

    public async Task<HandoverRecordResponse> OpenAsync(Guid vaultId, CancellationToken ct)
    {
        // 1. Người gọi phải là Người thực thi đang phụ trách kho
        Guid userId = currentUser.RequireUserId();
        Vault vault = await RequireExecutorVaultAsync(vaultId, userId, ct);

        // 2. Đã mở rồi thì trả biên bản cũ; kho phải đã kích hoạt
        HandoverRecord? existing = await records.FindByVaultIdAsync(vault.Id, ct);
        if (existing != null)
        {
            HandoverRecordResponse already = await BuildResponseAsync(existing, vault, null, ct);
            return already;
        }
        if (vault.Plan.Status != PlanStatus.Active)
        {
            throw HandoverAccess.NotAllowed("PLAN_NOT_ACTIVE",
                "Kho chưa kích hoạt nên chưa mở bàn giao được.");
        }

        // 3. Phân công một luật sư: không phải Chủ kho, Người thực thi hay người nhận của kho
        Guid? verifierId = await PickVerifierAsync(vault, ct);
        if (verifierId == null)
        {
            throw HandoverAccess.NotAllowed("NO_VERIFIER",
                "Hiện chưa có luật sư nào nhận được hồ sơ này. Vui lòng thử lại sau.");
        }

        // 4. Tạo biên bản, khóa cấu hình kho, lưu một lần
        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverRecord record = new HandoverRecord
        {
            VaultId = vault.Id,
            PlanId = vault.Plan.Id,
            Status = HandoverRecordStatus.Drafting,
            RevisionNo = 1,
            RequestedByUserId = userId,
            VerifierUserId = verifierId.Value,
            RequestedAt = now,
            UpdatedAt = now
        };
        records.Add(record);
        vault.Plan.Status = PlanStatus.HandoverRequested;
        vault.Plan.UpdatedAt = now;
        await SaveAsync(ct);

        HandoverRecordResponse response = await BuildResponseAsync(record, vault, null, ct);
        return response;
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
        RequireStatus(record, HandoverRecordStatus.Drafting, "Chỉ lấy file phân chia khi biên bản đang lập.");
        Vault vault = await RequireVaultAsync(record.VaultId, ct);

        // 2. Đọc file phân chia: mỗi người nhận của gói nhận mọi tài sản trong gói (gói nhiều người là đồng thừa kế)
        List<Designation> designations = await handover.FindStartableDesignationsAsync(vault.Id, ct);
        List<(Guid BeneficiaryId, Guid AssetId)> lines = new List<(Guid BeneficiaryId, Guid AssetId)>();
        HashSet<(Guid, Guid)> usedPairs = new HashSet<(Guid, Guid)>();
        foreach (Designation designation in designations)
        {
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
                "Chủ kho chưa lập file phân chia. Vui lòng tự lập biên bản.");
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
        string? error = await ValidateLinesAsync(vault.Id, lines, ct);
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
        string? error = await ValidateLinesAsync(vault.Id, lines, ct);
        if (error != null)
        {
            throw HandoverAccess.NotAllowed("RECORD_INVALID", error);
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
        Guid vaultId, List<(Guid BeneficiaryId, Guid AssetId)> lines, CancellationToken ct)
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
        return HandoverRecordRules.ValidateLines(lines, beneficiaryIds, assetIds);
    }

    private async Task<Guid?> PickVerifierAsync(Vault vault, CancellationToken ct)
    {
        List<Guid> excluded = new List<Guid>();
        excluded.Add(vault.OwnerId);
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
                CitizenIdLast4 = beneficiary.CitizenIdLast4
            };
            response.AvailableRecipients.Add(person);
        }

        // File phân chia của Chủ kho: gom chỉ định theo gói.
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
            ExecutorName = await FindUserNameAsync(record.RequestedByUserId, ct)
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
                recipient.CitizenIdLast4 = beneficiariesById[pair.Key].CitizenIdLast4;
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
