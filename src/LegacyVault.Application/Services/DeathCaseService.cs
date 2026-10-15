using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// SEC-04: người không phải Executor đang hiệu lực, hoặc là Owner, luôn nhận 404.
/// DATA-02, SEC-07: không ghi CCCD, họ tên, số giấy vào log hay message lỗi; response chỉ có 4 số cuối CCCD.
/// Mỗi use case gọi SaveChanges một lần (STATE-02).
/// </remarks>
public sealed class DeathCaseService : IDeathCaseService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IDeathCaseRepository deathCaseRepository;
    private readonly IExecutorReader executorReader;
    private readonly IDeathCaseVerifierPicker verifierPicker;
    private readonly IDeathCaseDataProtector dataProtector;
    private readonly IAssetContentStore contentStore;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public DeathCaseService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IDeathCaseRepository deathCaseRepository,
        IExecutorReader executorReader,
        IDeathCaseVerifierPicker verifierPicker,
        IDeathCaseDataProtector dataProtector,
        IAssetContentStore contentStore,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.deathCaseRepository = deathCaseRepository;
        this.executorReader = executorReader;
        this.verifierPicker = verifierPicker;
        this.dataProtector = dataProtector;
        this.contentStore = contentStore;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<List<ExecutorVaultResponse>> GetMyVaultsAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        List<ExecutorVaultRow> rows = await deathCaseRepository.FindExecutorVaultsAsync(userId, ct);
        List<ExecutorVaultResponse> responses = new List<ExecutorVaultResponse>();
        foreach (ExecutorVaultRow row in rows)
        {
            responses.Add(row.ToResponse());
        }
        return responses;
    }

    public async Task<OpenDeathCaseResult> OpenAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        if (vault == null || !await IsActiveExecutorAsync(vault, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }

        // DEATH-05: kho đã có hồ sơ đang mở (kể cả bản nháp) thì trả lại hồ sơ đó, không tạo thêm.
        DeathCase? existing = await deathCaseRepository.FindOpenByVaultIdAsync(vault.Id, ct);
        if (existing != null)
        {
            return BuildOpenResult(existing, false);
        }

        // DEATH-05: chỉ mở hồ sơ khi kế hoạch đang chạy. Dịch vụ ân hạn (chỉ đọc) vẫn mở được vì không kiểm gói dịch vụ.
        if (!DeathCaseRules.CanOpen(vault.Plan.Status))
        {
            throw PlanNotRunning(vault.Plan.Status);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DeathCase deathCase = new DeathCase();
        deathCase.VaultId = vault.Id;
        deathCase.PlanId = vault.Plan.Id;
        deathCase.Status = DeathCaseStatus.Draft;
        deathCase.CurrentVersionNo = 1;
        deathCase.OpenedByUserId = userId;
        deathCase.CreatedAt = now;

        // Nối với hồ sơ bị từ chối gần nhất (nếu có) để Verifier xem lịch sử.
        DeathCase? previous = await deathCaseRepository.FindLatestRejectedByVaultIdAsync(vault.Id, ct);
        if (previous != null)
        {
            deathCase.PreviousCaseId = previous.Id;
        }

        DeathCaseVersion version = new DeathCaseVersion();
        version.CaseId = deathCase.Id;
        version.VersionNo = 1;
        version.CreatedByUserId = userId;
        version.CreatedAt = now;
        deathCase.Versions.Add(version);

        // AC-71, AC-91: hai Executor mở cùng lúc — DB chỉ cho một hồ sơ mở mỗi kho; bên thua đọc lại hồ sơ bên thắng.
        bool added = await deathCaseRepository.TryAddAsync(deathCase, ct);
        if (added)
        {
            return BuildOpenResult(deathCase, true);
        }

        deathCaseRepository.DiscardPendingChanges();
        DeathCase? winner = await deathCaseRepository.FindOpenByVaultIdAsync(vault.Id, ct);
        if (winner == null)
        {
            // Hồ sơ bên thắng vừa bị từ chối ngay sau đó: hiếm; báo xung đột để FE gọi lại.
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Hồ sơ chứng tử vừa thay đổi. Vui lòng thử lại.");
        }
        return BuildOpenResult(winner, false);
    }

    public async Task<DeathCaseResponse> GetAsync(Guid caseId, CancellationToken ct)
    {
        CaseContext context = await LoadCaseForExecutorAsync(caseId, ct);
        return BuildResponse(context.DeathCase);
    }

    public async Task<DeathCaseResponse> UpdateDraftAsync(Guid caseId, UpdateDeathCaseDraftRequest request, CancellationToken ct)
    {
        CaseContext context = await LoadCaseForExecutorAsync(caseId, ct);
        DeathCase deathCase = context.DeathCase;
        if (!Etag.Matches(request.Etag, deathCase.UpdatedAt))
        {
            throw EditConflict();
        }

        // DEATH-04: chỉ sửa bản nháp mới nhất.
        DeathCaseVersion version = DeathCaseMappings.FindVersion(deathCase, deathCase.CurrentVersionNo);
        EnsureVersionEditable(deathCase, version);

        DeathCaseIdentityData? stored = dataProtector.Unprotect(version);
        DeathCaseIdentityData current = new DeathCaseIdentityData();
        if (stored != null)
        {
            current = stored;
        }
        DeathCaseIdentityData updated = new DeathCaseIdentityData();
        updated.OwnerFullName = ApplyText(current.OwnerFullName, request.OwnerFullName);
        updated.CertificateNumber = ApplyText(current.CertificateNumber, request.CertificateNumber);
        updated.IssuedBy = ApplyText(current.IssuedBy, request.IssuedBy);
        updated.OwnerCitizenId = current.OwnerCitizenId;
        if (request.OwnerCitizenId != null)
        {
            string? citizenId = TextInput.NullIfBlank(request.OwnerCitizenId);
            if (citizenId == null)
            {
                updated.OwnerCitizenId = null;
            }
            else
            {
                updated.OwnerCitizenId = CitizenIdRules.NormalizeCitizenId(citizenId, "ownerCitizenId");
            }
        }

        DateOnly? dateOfDeath = version.DateOfDeath;
        if (request.DateOfDeath != null)
        {
            dateOfDeath = request.DateOfDeath;
        }
        DateOnly? issuedOn = version.IssuedOn;
        if (request.IssuedOn != null)
        {
            issuedOn = request.IssuedOn;
        }

        // DEATH-01: ngày mất ≤ hôm nay; ngày cấp ≥ ngày mất và ≤ hôm nay (theo giờ Việt Nam).
        DateTimeOffset now = timeProvider.GetUtcNow();
        DeathCaseRules.ValidateDates(dateOfDeath, issuedOn, DeathCaseRules.TodayInVietnam(now));

        bool changed = !string.Equals(current.OwnerFullName, updated.OwnerFullName, StringComparison.Ordinal)
            || !string.Equals(current.OwnerCitizenId, updated.OwnerCitizenId, StringComparison.Ordinal)
            || !string.Equals(current.CertificateNumber, updated.CertificateNumber, StringComparison.Ordinal)
            || !string.Equals(current.IssuedBy, updated.IssuedBy, StringComparison.Ordinal)
            || dateOfDeath != version.DateOfDeath
            || issuedOn != version.IssuedOn;
        // Không có gì đổi: không mã hóa lại, không lưu, etag giữ nguyên.
        if (!changed)
        {
            return BuildResponse(deathCase);
        }

        // DATA-02: mỗi lần lưu dùng DEK mới.
        dataProtector.Protect(version, updated);
        version.DateOfDeath = dateOfDeath;
        version.IssuedOn = issuedOn;
        version.UpdatedAt = now;
        deathCase.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
        return BuildResponse(deathCase);
    }

    public async Task<DeathCaseResponse> AddFileAsync(Guid caseId, AddDeathCaseFileRequest request, CancellationToken ct)
    {
        CaseContext context = await LoadCaseForExecutorAsync(caseId, ct);
        DeathCase deathCase = context.DeathCase;
        DeathCaseVersion version = DeathCaseMappings.FindVersion(deathCase, deathCase.CurrentVersionNo);
        EnsureVersionEditable(deathCase, version);

        // NFR-09: tối đa 10 tệp mỗi phiên bản.
        if (version.Files.Count >= DeathCaseRules.MaxFilesPerVersion)
        {
            Dictionary<string, object?> limitExtensions = new Dictionary<string, object?>();
            limitExtensions["maxFiles"] = DeathCaseRules.MaxFilesPerVersion;
            throw new AppException(AppErrorType.Conflict, ErrorCode.DeathCaseFileLimitExceeded,
                "Mỗi bản hồ sơ chỉ được tối đa 10 trang giấy chứng tử.", limitExtensions);
        }
        if (request.Length <= 0)
        {
            throw ValidationErrors.For("file", "Tệp rỗng.");
        }
        if (request.Length > DeathCaseRules.MaxFileBytes)
        {
            throw FileTooLarge();
        }
        if (!request.Content.CanSeek)
        {
            throw ValidationErrors.For("file", "Không đọc được nội dung tệp để kiểm tra loại tệp.");
        }

        // NFR-09, SEC-12, AC-70: loại tệp theo nội dung thật (chữ ký đầu tệp), không tin đuôi tệp hay Content-Type.
        string contentType = await DetectContentTypeAsync(request.Content, ct);

        // Kho lưu trữ mã hóa nội dung trước khi ghi, tự đếm byte thật và dừng khi vượt 20 MiB (FILE_TOO_LARGE).
        // D8: không tính vào dung lượng kho nên không kiểm quota.
        StoredContent stored = await contentStore.SaveAsync(request.Content, DeathCaseRules.MaxFileBytes, ct);

        DateTimeOffset now = timeProvider.GetUtcNow();
        int nextPosition = 1;
        foreach (DeathCaseFile existing in version.Files)
        {
            if (existing.Position >= nextPosition)
            {
                nextPosition = existing.Position + 1;
            }
        }

        DeathCaseFile file = new DeathCaseFile();
        file.VersionId = version.Id;
        file.Position = nextPosition;
        file.FileName = DeathCaseFileRules.SanitizeFileName(request.FileName);
        file.ContentType = contentType;
        file.SizeBytes = stored.SizeBytes;
        file.Sha256 = stored.Sha256;
        file.StorageKey = stored.StorageKey;
        file.EncryptionKeyId = stored.EncryptionKeyId;
        file.WrappedDataKey = stored.WrappedDataKey;
        file.UploadedByUserId = context.UserId;
        file.UploadedAt = now;
        version.Files.Add(file);
        deathCaseRepository.AddFile(file);
        version.UpdatedAt = now;
        deathCase.UpdatedAt = now;
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Bù trừ: không để nội dung không có bản ghi trong kho lưu trữ. Không dùng ct vì request có thể đã bị hủy.
            await contentStore.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }
        return BuildResponse(deathCase);
    }

    public async Task<DeathCaseResponse> RemoveFileAsync(Guid caseId, Guid fileId, CancellationToken ct)
    {
        CaseContext context = await LoadCaseForExecutorAsync(caseId, ct);
        DeathCase deathCase = context.DeathCase;
        DeathCaseVersion version = DeathCaseMappings.FindVersion(deathCase, deathCase.CurrentVersionNo);
        // DEATH-04: file của bản đã gửi không gỡ được.
        EnsureVersionEditable(deathCase, version);

        DeathCaseFile? target = null;
        foreach (DeathCaseFile file in version.Files)
        {
            if (file.Id == fileId)
            {
                target = file;
            }
        }
        if (target == null)
        {
            throw CaseNotFound();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string storageKey = target.StorageKey;
        version.Files.Remove(target);
        deathCaseRepository.RemoveFile(target);
        version.UpdatedAt = now;
        deathCase.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        // D8: nội dung có thể được dùng chung với phiên bản khác; chỉ xóa khi không còn dòng nào dùng StorageKey này.
        int remaining = await deathCaseRepository.CountFilesUsingStorageKeyAsync(storageKey, ct);
        if (remaining == 0)
        {
            await contentStore.DeleteAsync(storageKey, CancellationToken.None);
        }
        return BuildResponse(deathCase);
    }

    public async Task<DeathCaseResponse> SubmitAsync(Guid caseId, SubmitDeathCaseRequest request, CancellationToken ct)
    {
        CaseContext context = await LoadCaseForExecutorAsync(caseId, ct);
        DeathCase deathCase = context.DeathCase;
        Vault vault = context.Vault;
        HandoverPlan plan = vault.Plan;
        DeathCaseVersion version = DeathCaseMappings.FindVersion(deathCase, deathCase.CurrentVersionNo);

        // AC-71: bản hiện tại đã được xác nhận rồi (bấm lặp, hoặc Executor kia vừa gửi) → trả trạng thái hiện tại.
        if (version.SubmittedAt != null)
        {
            return BuildResponse(deathCase);
        }
        if (!request.Confirm)
        {
            throw ValidationErrors.For("confirm", "Vui lòng xác nhận thông tin trong hồ sơ là đúng trước khi gửi.");
        }
        if (!Etag.Matches(request.Etag, deathCase.UpdatedAt))
        {
            throw EditConflict();
        }
        EnsureVersionEditable(deathCase, version);

        // DEATH-01: đủ 6 trường và ít nhất một trang giấy.
        DeathCaseIdentityData? identity = dataProtector.Unprotect(version);
        DeathCaseIdentityData data = new DeathCaseIdentityData();
        if (identity != null)
        {
            data = identity;
        }
        List<string> missing = DeathCaseRules.GetMissingRequirements(
            data.OwnerFullName, data.OwnerCitizenId, version.DateOfDeath,
            data.CertificateNumber, version.IssuedOn, data.IssuedBy, version.Files.Count);
        if (missing.Count > 0)
        {
            Dictionary<string, object?> incompleteExtensions = new Dictionary<string, object?>();
            incompleteExtensions["missingRequirements"] = missing;
            throw new AppException(AppErrorType.Conflict, ErrorCode.DeathCaseIncomplete,
                "Hồ sơ chứng tử chưa đủ thông tin hoặc chưa có trang giấy nào.", incompleteExtensions);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        int submittingVersionNo = version.VersionNo;

        // DEATH-05, DEATH-12 (phần mở), SETUP-05: lần gửi đầu mới khóa kế hoạch và ghim trạng thái trước đó (D1, D2).
        // Không dừng lịch điểm danh: nhánh B cần kỳ đang mở để tính lại khi từ chối.
        // Kế hoạch dùng RowVersion: Owner điểm danh hoặc job điểm danh xen giữa thì lần lưu này báo EDIT_CONFLICT.
        bool isFirstSubmit = deathCase.FirstSubmittedAt == null;
        if (isFirstSubmit)
        {
            if (!PlanRules.IsRunning(plan.Status))
            {
                throw PlanNotRunning(plan.Status);
            }
            deathCase.StatusBeforeCase = plan.Status;
            deathCase.FirstSubmittedAt = now;
            plan.Status = PlanStatus.DeathCaseOpen;
            plan.UpdatedAt = now;
        }

        // DEATH-04: xác nhận bản này; bản đã gửi không bao giờ sửa lại.
        version.SubmittedAt = now;
        version.SubmittedByUserId = context.UserId;
        version.PlanConfigVersion = plan.ConfigVersion;
        version.UpdatedAt = now;
        deathCase.ReviewVersionNo = version.VersionNo;
        deathCase.Status = DeathCaseStatus.UnderReview;

        // DEATH-10: có điểm danh sau ngày mất ghi trên giấy → đặt cờ, chặn duyệt tới khi Verifier xem xét.
        // version.DateOfDeath chắc chắn có giá trị vì hồ sơ đã đủ trường.
        DateTimeOffset cutoff = DeathCaseRules.CheckInCutoffUtc(version.DateOfDeath!.Value);
        bool checkedInAfterDeath = await deathCaseRepository.HasCheckInAtOrAfterAsync(plan.Id, cutoff, ct);
        if (checkedInAfterDeath)
        {
            DeathCaseFlag flag = new DeathCaseFlag();
            flag.CaseId = deathCase.Id;
            flag.VersionId = version.Id;
            flag.Kind = DeathCaseFlagKind.CheckinAfterDeath;
            flag.Detail = "Owner có điểm danh sau ngày mất ghi trên giấy chứng tử.";
            flag.CreatedAt = now;
            deathCase.Flags.Add(flag);
            deathCaseRepository.AddFlag(flag);
        }

        // DEATH-08, DEATH-11: lần đầu, hoặc đổi trọng yếu so với bản đã báo → xếp thông báo Pending (outbox, D3)
        // và dừng đồng hồ 72 giờ cho tới khi job gửi được thư. Đổi không trọng yếu thì giữ nguyên đồng hồ.
        if (NeedsOwnerNotice(deathCase, data, version))
        {
            DeathCaseNotice notice = new DeathCaseNotice();
            notice.CaseId = deathCase.Id;
            notice.VersionNo = version.VersionNo;
            notice.Channel = DeathCaseNoticeChannel.Email;
            notice.RecipientUserId = vault.OwnerId;
            notice.Status = DeathCaseNoticeStatus.Pending;
            notice.CreatedAt = now;
            deathCase.Notices.Add(notice);
            deathCaseRepository.AddNotice(notice);
            deathCase.NotifiedVersionNo = version.VersionNo;
            deathCase.ObjectionStartsAt = null;
            deathCase.ObjectionEndsAt = null;
        }

        // ASSIGN-01, ROLE-03 (D4): chưa có phân công còn hiệu lực thì chọn Verifier xoay vòng, loại Owner,
        // Executor đang hiệu lực của kho và người gửi. Không ai phù hợp → để trống.
        if (deathCase.Assignments.Count == 0)
        {
            await AssignVerifierAsync(deathCase, vault, context.UserId, now, ct);
        }
        else if (!isFirstSubmit)
        {
            // ASSIGN-04, D1, TIME-07: Executor gửi bản mới thì hạn xét của phân công đang hiệu lực tính lại từ lúc gửi
            // (giữ AssignedAt), nhưng không sớm hơn 48 giờ sau khi hết phản đối. Chạy sau đoạn xếp thông báo ở trên:
            // đổi trọng yếu đã đặt ObjectionEndsAt = null nên hạn = lúc gửi + 48 giờ.
            foreach (DeathCaseAssignment assignment in deathCase.Assignments)
            {
                if (assignment.EndedAt == null)
                {
                    assignment.DueAt = DeathCaseAssignmentRules.ComputeDueAt(now, deathCase.ObjectionEndsAt);
                }
            }
        }

        deathCase.UpdatedAt = now;
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsSubmitRace(ex))
        {
            // AC-71: Executor khác vừa gửi cùng bản này (etag/RowVersion lệch, hoặc trùng cờ/phân công)
            // → trả trạng thái hiện tại thay vì báo lỗi.
            deathCaseRepository.DiscardPendingChanges();
            DeathCase? latest = await deathCaseRepository.FindByIdAsync(caseId, ct);
            if (latest != null)
            {
                DeathCaseVersion latestVersion = DeathCaseMappings.FindVersion(latest, submittingVersionNo);
                if (latestVersion.SubmittedAt != null)
                {
                    return BuildResponse(latest);
                }
            }
            throw;
        }
        return BuildResponse(deathCase);
    }

    public async Task<DeathCaseResponse> CreateVersionAsync(Guid caseId, CreateDeathCaseVersionRequest request, CancellationToken ct)
    {
        CaseContext context = await LoadCaseForExecutorAsync(caseId, ct);
        DeathCase deathCase = context.DeathCase;
        if (!Etag.Matches(request.Etag, deathCase.UpdatedAt))
        {
            throw EditConflict();
        }

        // D5: chỉ tạo bản mới từ bản đã gửi, khi hồ sơ đang xét hoặc cần bổ sung, và chưa có bản nháp nào.
        DeathCaseVersion current = DeathCaseMappings.FindVersion(deathCase, deathCase.CurrentVersionNo);
        if (!DeathCaseRules.CanCreateNewVersion(deathCase, current))
        {
            throw NotAllowed(DeathCaseNotAllowedReason.CaseNotRevisable);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DeathCaseVersion next = new DeathCaseVersion();
        next.CaseId = deathCase.Id;
        next.VersionNo = current.VersionNo + 1;
        next.DateOfDeath = current.DateOfDeath;
        next.IssuedOn = current.IssuedOn;
        next.CreatedByUserId = context.UserId;
        next.CreatedAt = now;

        // DATA-02: chép dữ liệu nhạy cảm sang bản mới và mã hóa lại bằng DEK mới (AAD gắn Id của bản mới).
        DeathCaseIdentityData? identity = dataProtector.Unprotect(current);
        if (identity != null)
        {
            dataProtector.Protect(next, identity);
        }

        // D8: chép dòng file, dùng chung nội dung đã lưu (cùng StorageKey); không ghi lại nội dung.
        foreach (DeathCaseFile source in current.Files)
        {
            DeathCaseFile copy = new DeathCaseFile();
            copy.VersionId = next.Id;
            copy.Position = source.Position;
            copy.FileName = source.FileName;
            copy.ContentType = source.ContentType;
            copy.SizeBytes = source.SizeBytes;
            copy.Sha256 = source.Sha256;
            copy.StorageKey = source.StorageKey;
            copy.EncryptionKeyId = source.EncryptionKeyId;
            copy.WrappedDataKey = source.WrappedDataKey;
            copy.UploadedByUserId = source.UploadedByUserId;
            copy.UploadedAt = source.UploadedAt;
            copy.CopiedFromFileId = source.Id;
            next.Files.Add(copy);
        }

        // Trạng thái hồ sơ và ReviewVersionNo giữ nguyên: Verifier vẫn xét bản đã gửi gần nhất.
        deathCase.Versions.Add(next);
        deathCaseRepository.AddVersion(next);
        foreach (DeathCaseFile copy in next.Files)
        {
            deathCaseRepository.AddFile(copy);
        }
        deathCase.CurrentVersionNo = next.VersionNo;
        deathCase.UpdatedAt = now;
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (deathCaseRepository.IsUniqueViolation(ex))
        {
            // Hai Executor cùng tạo bản mới: bên kia lưu trước.
            deathCaseRepository.DiscardPendingChanges();
            throw EditConflict();
        }
        return BuildResponse(deathCase);
    }

    /// <summary>Lỗi lưu do request khác ghi trước: xung đột phiên bản (EDIT_CONFLICT) hoặc vi phạm chỉ mục unique.</summary>
    private bool IsSubmitRace(Exception exception)
    {
        AppException? appException = exception as AppException;
        if (appException != null && appException.Code == ErrorCode.EditConflict)
        {
            return true;
        }
        return deathCaseRepository.IsUniqueViolation(exception);
    }

    /// <summary>
    /// true khi cần báo Owner: chưa báo lần nào, hoặc bản đang gửi đổi danh tính Owner/ngày mất so với bản đã báo (DEATH-11).
    /// </summary>
    private bool NeedsOwnerNotice(DeathCase deathCase, DeathCaseIdentityData newData, DeathCaseVersion newVersion)
    {
        if (deathCase.NotifiedVersionNo == null)
        {
            return true;
        }

        DeathCaseVersion notifiedVersion = DeathCaseMappings.FindVersion(deathCase, deathCase.NotifiedVersionNo.Value);
        DeathCaseIdentityData oldData = new DeathCaseIdentityData();
        DeathCaseIdentityData? stored = dataProtector.Unprotect(notifiedVersion);
        if (stored != null)
        {
            oldData = stored;
        }
        return DeathCaseRules.IsCriticalChange(
            oldData.OwnerFullName, oldData.OwnerCitizenId, notifiedVersion.DateOfDeath,
            newData.OwnerFullName, newData.OwnerCitizenId, newVersion.DateOfDeath);
    }

    private async Task AssignVerifierAsync(
        DeathCase deathCase, Vault vault, Guid submitterUserId, DateTimeOffset now, CancellationToken ct)
    {
        // D5: danh sách loại trừ nằm ở IDeathCaseVerifierPicker, dùng chung với job phân công.
        Guid? verifierId = await verifierPicker.PickAsync(vault, submitterUserId, null, ct);
        if (verifierId == null)
        {
            // ASSIGN-04 (nhánh B): chưa có Verifier phù hợp thì hồ sơ chờ được giao sau.
            return;
        }

        DeathCaseAssignment assignment = new DeathCaseAssignment();
        assignment.CaseId = deathCase.Id;
        assignment.VerifierUserId = verifierId.Value;
        assignment.AssignedAt = now;
        assignment.DueAt = DeathCaseAssignmentRules.ComputeDueAt(now, deathCase.ObjectionEndsAt);
        deathCase.Assignments.Add(assignment);
        deathCaseRepository.AddAssignment(assignment);
    }

    /// <summary>Đọc tối đa 12 byte đầu để nhận loại tệp rồi tua stream về đầu. Loại không hỗ trợ → 400.</summary>
    private static async Task<string> DetectContentTypeAsync(Stream content, CancellationToken ct)
    {
        byte[] buffer = new byte[DeathCaseFileRules.HeaderLength];
        content.Seek(0, SeekOrigin.Begin);
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await content.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
            {
                break;
            }
            total = total + read;
        }
        content.Seek(0, SeekOrigin.Begin);

        byte[] header = new byte[total];
        Array.Copy(buffer, header, total);
        string? contentType = DeathCaseFileRules.DetectContentType(header);
        if (contentType == null)
        {
            throw ValidationErrors.For("file", "Chỉ nhận tệp PDF, JPEG, PNG hoặc WEBP (kiểm tra theo nội dung tệp).");
        }
        return contentType;
    }

    private static AppException FileTooLarge()
    {
        return new AppException(AppErrorType.PayloadTooLarge, ErrorCode.FileTooLarge,
            "Tệp vượt quá 20 MiB.");
    }

    /// <summary>
    /// Hồ sơ theo Id mà người gọi là Executor đang hiệu lực của kho. Không có, hoặc không đủ quyền → 404
    /// DEATH_CASE_NOT_FOUND (SEC-04). Trả kèm kho (đã nạp Plan) cho các bước sau.
    /// </summary>
    private async Task<CaseContext> LoadCaseForExecutorAsync(Guid caseId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DeathCase? deathCase = await deathCaseRepository.FindByIdAsync(caseId, ct);
        if (deathCase == null)
        {
            throw CaseNotFound();
        }
        Vault? vault = await vaultRepository.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null || !await IsActiveExecutorAsync(vault, userId, ct))
        {
            throw CaseNotFound();
        }

        CaseContext context = new CaseContext();
        context.DeathCase = deathCase;
        context.Vault = vault;
        context.UserId = userId;
        return context;
    }

    /// <summary>ROLE-03: Owner không bao giờ là Executor của chính kho; chỉ Executor Accepted mới tính.</summary>
    private async Task<bool> IsActiveExecutorAsync(Vault vault, Guid userId, CancellationToken ct)
    {
        if (vault.OwnerId == userId)
        {
            return false;
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// DEATH-04: bản đã gửi không bao giờ sửa. Hồ sơ đã đóng (duyệt/từ chối/tạm dừng) → CASE_CLOSED,
    /// còn lại → VERSION_LOCKED.
    /// </summary>
    private static void EnsureVersionEditable(DeathCase deathCase, DeathCaseVersion version)
    {
        if (DeathCaseRules.CanEditVersion(deathCase, version))
        {
            return;
        }
        if (deathCase.Status == DeathCaseStatus.ApprovedForHandover
            || deathCase.Status == DeathCaseStatus.Rejected
            || deathCase.Status == DeathCaseStatus.Suspended)
        {
            throw NotAllowed(DeathCaseNotAllowedReason.CaseClosed);
        }
        throw NotAllowed(DeathCaseNotAllowedReason.VersionLocked);
    }

    /// <summary>null = giữ nguyên, "" hoặc chỉ khoảng trắng = xóa, còn lại = giá trị đã cắt khoảng trắng đầu/cuối.</summary>
    private static string? ApplyText(string? current, string? requested)
    {
        if (requested == null)
        {
            return current;
        }
        return TextInput.NullIfBlank(requested);
    }

    private OpenDeathCaseResult BuildOpenResult(DeathCase deathCase, bool created)
    {
        OpenDeathCaseResult result = new OpenDeathCaseResult();
        result.Created = created;
        result.DeathCase = BuildResponse(deathCase);
        return result;
    }

    /// <summary>Giải mã bản hiện tại rồi dựng response (chỉ 4 số cuối CCCD).</summary>
    private DeathCaseResponse BuildResponse(DeathCase deathCase)
    {
        DeathCaseVersion current = DeathCaseMappings.FindVersion(deathCase, deathCase.CurrentVersionNo);
        DeathCaseIdentityData? identity = dataProtector.Unprotect(current);
        return deathCase.ToResponse(identity);
    }

    private static AppException CaseNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
    }

    private static AppException EditConflict()
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
            "Hồ sơ chứng tử vừa được sửa ở nơi khác. Vui lòng tải lại rồi sửa tiếp.");
    }

    private static AppException NotAllowed(DeathCaseNotAllowedReason reason)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason.ToApiString();
        return new AppException(AppErrorType.Conflict, ErrorCode.DeathCaseNotAllowed,
            "Hồ sơ chứng tử hiện không cho phép thao tác này.", extensions);
    }

    private static AppException PlanNotRunning(PlanStatus planStatus)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = DeathCaseNotAllowedReason.PlanNotRunning.ToApiString();
        extensions["planStatus"] = planStatus.ToApiString();
        return new AppException(AppErrorType.Conflict, ErrorCode.DeathCaseNotAllowed,
            "Kế hoạch bàn giao chưa chạy nên chưa thể mở hồ sơ chứng tử.", extensions);
    }

    /// <summary>Hồ sơ đã nạp cùng kho (kèm Plan) và người gọi.</summary>
    private sealed class CaseContext
    {
        public DeathCase DeathCase { get; set; } = null!;

        public Vault Vault { get; set; } = null!;

        public Guid UserId { get; set; }
    }
}
