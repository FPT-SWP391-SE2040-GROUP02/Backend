using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// - BR-50 sửa 25/10/2026: ảnh selfie của lần thử gần nhất (đạt hoặc không) được lưu MÃ HÓA trên lời báo để Verifier
///   đối chiếu với ảnh CCCD lúc eKYC; lần thử mới thay ảnh cũ. Lỗi kỹ thuật không lưu ảnh.
/// - ALIVE-01: báo lần đầu chặn ngay trong MỘT giao dịch: hồ sơ sang Suspended, kế hoạch sang Blocked (chặn nhận lượt,
///   đồng ý, tải), grant đang có sang Blocked. HandoverService ghi plan.UpdatedAt khi nhận/đồng ý nên thao tác nhận
///   chạy cùng lúc với báo sẽ thua RowVersion của kế hoạch (AC-34).
/// - ALIVE-09: báo lặp chỉ tăng bộ đếm, không đổi hạn, không tạo hold trùng (chỉ mục unique có lọc).
/// - Selfie so với mẫu khuôn mặt qua IOwnerFaceVerifier; lỗi kỹ thuật không tính là thất bại (EKYC-19).
/// - M2, ALIVE-09: sau khi bị bác, Owner mở lại bằng selfie mới đạt (ReopenAfterRejectionAsync); báo trơn vẫn bị từ chối.
/// - TODO(LV-07): báo Verifier/Executor bằng thông báo (sự cố theo dõi đã được mở cùng lúc chặn).
/// </remarks>
public sealed class OwnerObjectionService : IOwnerObjectionService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IDeathCaseRepository deathCases;
    private readonly IObjectionRepository objections;
    private readonly IOwnerFaceVerifier faceVerifier;
    private readonly IHoldService holdService;
    private readonly IIncidentService incidentService;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IAssetContentStore contentStore;

    public OwnerObjectionService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IDeathCaseRepository deathCases,
        IObjectionRepository objections,
        IOwnerFaceVerifier faceVerifier,
        IHoldService holdService,
        IIncidentService incidentService,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IAssetContentStore contentStore)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.deathCases = deathCases;
        this.objections = objections;
        this.faceVerifier = faceVerifier;
        this.holdService = holdService;
        this.incidentService = incidentService;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.contentStore = contentStore;
    }

    public async Task<OwnerDeathCaseResponse> GetAsync(Guid vaultId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        DeathCase deathCase = await LoadSubmittedCaseAsync(vault.Id, ct);
        return await BuildAsync(deathCase, ct);
    }

    public async Task<OwnerDeathCaseResponse> ReportAsync(Guid vaultId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        DeathCase deathCase = await LoadSubmittedCaseAsync(vault.Id, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();

        DeathCaseObjection? open = await objections.FindOpenByCaseAsync(deathCase.Id, ct);
        if (open != null)
        {
            // ALIVE-09: báo lặp chỉ ghi thêm sự kiện; hạn và hold giữ nguyên. Không đổi UpdatedAt của hồ sơ.
            open.ReportCount = open.ReportCount + 1;
            open.LastReportedAt = now;
            await unitOfWork.SaveChangesAsync(ct);
            return await BuildAsync(deathCase, ct);
        }

        DeathCaseObjection? latest = await objections.FindLatestByCaseAsync(deathCase.Id, ct);
        if (latest != null && latest.Conclusion == ObjectionConclusion.ObjectionRejected)
        {
            // ALIVE-09: sau khi bị bác, muốn mở lại phải có bằng chứng mới: nộp selfie mới qua POST objection/selfie
            // (ReopenAfterRejectionAsync). Báo trơn không đủ.
            throw ObjectionMapper.NotAllowed(DeathCaseNotAllowedReason.ObjectionAlreadyRejected);
        }
        if (!ObjectionRules.CanReport(deathCase.Status))
        {
            throw ObjectionMapper.CaseNotFound();
        }

        await OpenObjectionAsync(vault, deathCase, now, false, null, ct);
        return await BuildAsync(deathCase, ct);
    }

    /// <summary>
    /// Tạo lời báo mới và chặn ngay hồ sơ, kế hoạch, grant, hold, sự cố trong MỘT giao dịch (ALIVE-01, ALIVE-07).
    /// selfiePassed = true khi mở lại sau khi bị bác: selfie mới đạt chính là bằng chứng mới (ALIVE-09); khi đó
    /// selfieImage là ảnh của lần thử đạt đó, lưu cùng lời báo mới (BR-50 sửa).
    /// </summary>
    private async Task OpenObjectionAsync(
        Vault vault, DeathCase deathCase, DateTimeOffset now, bool selfiePassed, byte[]? selfieImage, CancellationToken ct)
    {
        DeathCaseObjection objection = new DeathCaseObjection();
        objection.CaseId = deathCase.Id;
        objection.VaultId = vault.Id;
        objection.OwnerUserId = vault.OwnerId;
        objection.Status = ObjectionStatus.Open;
        objection.FirstReportedAt = now;
        objection.LastReportedAt = now;
        objection.ReportCount = 1;
        objection.SelfieDeadlineAt = now + ObjectionRules.SelfieWindow;
        objection.PreviousCaseStatus = deathCase.Status;
        objection.PreviousPlanStatus = vault.Plan.Status;
        if (selfiePassed)
        {
            // M2, ALIVE-09: mở lại sau khi bị bác bằng phiên selfie mới đạt (bằng chứng mới).
            objection.SelfiePassedAt = now;
        }
        objection.VerifierUserId = FindActiveVerifier(deathCase);
        if (objection.VerifierUserId != null)
        {
            // D2a, SRS §6.5: Verifier kết luận trong 48 giờ sau hạn selfie.
            objection.VerifierDueAt = ObjectionRules.ComputeVerifierDueAt(now, objection.SelfieDeadlineAt);
        }

        // Chặn ngay: hồ sơ, kế hoạch và mọi grant đang có (ALIVE-01, ALIVE-07).
        deathCase.Status = DeathCaseStatus.Suspended;
        deathCase.UpdatedAt = now;
        vault.Plan.Status = PlanStatus.Blocked;
        vault.Plan.UpdatedAt = now;
        List<HandoverTurn> turns = await objections.FindTurnsWithGrantsAsync(vault.Plan.Id, ct);
        foreach (HandoverTurn turn in turns)
        {
            if (turn.Grant != null && turn.Grant.Status == HandoverGrantStatus.Active)
            {
                turn.Grant.Status = HandoverGrantStatus.Blocked;
            }
        }
        // Hold chung + sự cố theo dõi có người phụ trách và hạn (ALIVE-01, SRS §6.4); lưu cùng giao dịch với việc chặn.
        await holdService.PlaceOwnerAliveAsync(vault, objection.VerifierUserId, objection.SelfieDeadlineAt, ct);
        await incidentService.RaiseAsync(IncidentType.OwnerAlive, vault.Id, IncidentRules.TargetDeathCase, deathCase.Id,
            "Owner báo còn sống, chờ Verifier kết luận.", objection.VerifierUserId, ct);
        audit.Add(AuditRules.Create(vault.Id, vault.OwnerId, AuditActorRole.Owner, AuditAction.ObjectionReported,
            AuditTargetType.DeathCase, deathCase.Id, null, AuditOutcome.Succeeded, null, now));
        SelfieImageChange? imageChange = null;
        if (selfieImage != null)
        {
            imageChange = await AttachSelfieImageAsync(objection, selfieImage, true, now, ct);
        }
        objections.Add(objection);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (objections.IsUniqueViolation(ex))
        {
            await DiscardNewSelfieImageAsync(imageChange);
            throw ObjectionMapper.EditConflict();
        }
        catch
        {
            await DiscardNewSelfieImageAsync(imageChange);
            throw;
        }
    }

    public async Task<OwnerDeathCaseResponse> SubmitSelfieAsync(
        Guid vaultId, Stream content, string fileName, string contentType, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        DeathCase deathCase = await LoadSubmittedCaseAsync(vault.Id, ct);
        DeathCaseObjection? objection = await objections.FindOpenByCaseAsync(deathCase.Id, ct);
        if (objection == null)
        {
            return await ReopenAfterRejectionAsync(vault, deathCase, content, fileName, contentType, ct);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (objection.SelfiePassedAt != null)
        {
            return await BuildAsync(deathCase, ct);
        }
        if (!ObjectionRules.IsSelfieWindowOpen(objection, now))
        {
            throw ObjectionMapper.NotAllowed(DeathCaseNotAllowedReason.ObjectionWindowClosed);
        }
        if (ObjectionRules.RecentFailures(objection, now) >= ObjectionRules.MaxSelfieFailures)
        {
            throw ObjectionMapper.NotAllowed(DeathCaseNotAllowedReason.SelfieAttemptsExceeded);
        }

        byte[] image = await ReadImageAsync(content, contentType, ct);
        OwnerFaceOutcome outcome = await faceVerifier.VerifyAsync(vault.OwnerId, image, fileName, contentType, ct);
        if (outcome == OwnerFaceOutcome.NoTemplate)
        {
            throw FaceTemplateMissing();
        }
        if (outcome == OwnerFaceOutcome.Unavailable)
        {
            throw new AppException(AppErrorType.ServiceUnavailable, ErrorCode.EkycServiceUnavailable,
                "Chưa so khớp được khuôn mặt lúc này. Vui lòng thử lại sau.");
        }

        bool matched = outcome == OwnerFaceOutcome.Matched;
        if (matched)
        {
            objection.SelfiePassedAt = now;
        }
        else
        {
            ObjectionRules.RecordFailure(objection, now);
        }
        SelfieImageChange? imageChange = await AttachSelfieImageAsync(objection, image, matched, now, ct);
        objection.UpdatedAt = now;
        await SaveWithSelfieImageAsync(imageChange, ct);
        return await BuildAsync(deathCase, ct);
    }

    /// <summary>
    /// Owner chưa có mẫu khuôn mặt vì chưa hoàn tất eKYC. Giữ mã 503 EKYC_SERVICE_UNAVAILABLE (không tính thất bại, EKYC-19)
    /// nhưng thêm reason để FE báo đúng việc cần làm thay vì nói dịch vụ chưa sẵn sàng.
    /// </summary>
    private static AppException FaceTemplateMissing()
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = "FACE_TEMPLATE_MISSING";
        return new AppException(AppErrorType.ServiceUnavailable, ErrorCode.EkycServiceUnavailable,
            "Chủ kho chưa có mẫu khuôn mặt vì chưa hoàn tất xác minh eKYC.", extensions);
    }

    /// <summary>
    /// M2, ALIVE-09: lời báo trước đã bị bác thì Owner chỉ mở lại được bằng bằng chứng mới là phiên selfie mới khớp mẫu khuôn mặt.
    /// Selfie khớp: mở lời báo mới đã có selfie đạt (chặn lại như lần đầu). Không khớp: ghi thất bại trên lời báo cũ
    /// để vẫn có giới hạn 3 lần trong 24 giờ (EKYC-20). Chưa từng báo hoặc hồ sơ không còn báo được thì 404.
    /// </summary>
    private async Task<OwnerDeathCaseResponse> ReopenAfterRejectionAsync(
        Vault vault, DeathCase deathCase, Stream content, string fileName, string contentType, CancellationToken ct)
    {
        DeathCaseObjection? latest = await objections.FindLatestByCaseAsync(deathCase.Id, ct);
        if (latest == null || latest.Conclusion != ObjectionConclusion.ObjectionRejected
            || !ObjectionRules.CanReport(deathCase.Status))
        {
            throw ObjectionMapper.CaseNotFound();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (ObjectionRules.RecentFailures(latest, now) >= ObjectionRules.MaxSelfieFailures)
        {
            throw ObjectionMapper.NotAllowed(DeathCaseNotAllowedReason.SelfieAttemptsExceeded);
        }

        byte[] image = await ReadImageAsync(content, contentType, ct);
        OwnerFaceOutcome outcome = await faceVerifier.VerifyAsync(vault.OwnerId, image, fileName, contentType, ct);
        if (outcome == OwnerFaceOutcome.NoTemplate)
        {
            throw FaceTemplateMissing();
        }
        if (outcome == OwnerFaceOutcome.Unavailable)
        {
            throw new AppException(AppErrorType.ServiceUnavailable, ErrorCode.EkycServiceUnavailable,
                "Chưa so khớp được khuôn mặt lúc này. Vui lòng thử lại sau.");
        }

        if (outcome != OwnerFaceOutcome.Matched)
        {
            ObjectionRules.RecordFailure(latest, now);
            SelfieImageChange? imageChange = await AttachSelfieImageAsync(latest, image, false, now, ct);
            latest.UpdatedAt = now;
            await SaveWithSelfieImageAsync(imageChange, ct);
            return await BuildAsync(deathCase, ct);
        }

        await OpenObjectionAsync(vault, deathCase, now, true, image, ct);
        return await BuildAsync(deathCase, ct);
    }

    /// <summary>
    /// Lưu MÃ HÓA ảnh selfie của lần thử vừa so khớp lên lời báo, thay ảnh cũ (BR-50 sửa). Ảnh không phải JPEG/PNG/WebP
    /// theo nội dung thật thì giữ nguyên ảnh cũ và trả null. Trả khóa mới (để bù trừ) và khóa cũ (để xóa sau khi lưu).
    /// </summary>
    private async Task<SelfieImageChange?> AttachSelfieImageAsync(
        DeathCaseObjection objection, byte[] image, bool matched, DateTimeOffset now, CancellationToken ct)
    {
        string? contentType = IdentityImageStorage.DetectContentType(image);
        if (contentType == null)
        {
            return null;
        }
        StoredContent stored = await IdentityImageStorage.SaveAsync(contentStore, image, ct);

        SelfieImageChange change = new SelfieImageChange();
        change.NewStorageKey = stored.StorageKey;
        change.OldStorageKey = objection.SelfieImageStorageKey;

        objection.SelfieImageStorageKey = stored.StorageKey;
        objection.SelfieImageContentType = contentType;
        objection.SelfieImageSizeBytes = stored.SizeBytes;
        objection.SelfieImageEncryptionKeyId = stored.EncryptionKeyId;
        objection.SelfieImageWrappedDataKey = stored.WrappedDataKey;
        objection.SelfieImageCapturedAt = now;
        objection.SelfieImageMatched = matched;
        return change;
    }

    /// <summary>Lưu DB; lỗi thì xóa ảnh mới vừa ghi rồi ném lại, thành công thì xóa ảnh cũ đã bị thay.</summary>
    private async Task SaveWithSelfieImageAsync(SelfieImageChange? imageChange, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await DiscardNewSelfieImageAsync(imageChange);
            throw;
        }
        if (imageChange != null)
        {
            await IdentityImageStorage.TryDeleteAsync(contentStore, imageChange.OldStorageKey);
        }
    }

    private async Task DiscardNewSelfieImageAsync(SelfieImageChange? imageChange)
    {
        if (imageChange != null)
        {
            await IdentityImageStorage.TryDeleteAsync(contentStore, imageChange.NewStorageKey);
        }
    }

    /// <summary>Khóa lưu trữ của ảnh selfie vừa ghi và của ảnh cũ bị thay (null khi chưa có ảnh cũ).</summary>
    private sealed class SelfieImageChange
    {
        public string NewStorageKey { get; set; } = string.Empty;

        public string? OldStorageKey { get; set; }
    }

    private async Task<OwnerDeathCaseResponse> BuildAsync(DeathCase deathCase, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        OwnerDeathCaseResponse response = new OwnerDeathCaseResponse();
        response.CaseId = deathCase.Id;
        response.CaseCode = ObjectionMapper.CaseCode(deathCase.Id);
        response.Status = deathCase.Status.ToApiString();
        response.ObjectionStartsAt = deathCase.ObjectionStartsAt;
        response.ObjectionEndsAt = deathCase.ObjectionEndsAt;
        DeathCaseObjection? latest = await objections.FindLatestByCaseAsync(deathCase.Id, ct);
        if (latest != null)
        {
            response.Objection = ObjectionMapper.ToResponse(latest, now);
        }
        return response;
    }

    /// <summary>Hồ sơ đang mở của kho đã được gửi (không phải bản nháp); không có → 404.</summary>
    private async Task<DeathCase> LoadSubmittedCaseAsync(Guid vaultId, CancellationToken ct)
    {
        DeathCase? deathCase = await deathCases.FindOpenByVaultIdAsync(vaultId, ct);
        if (deathCase == null || deathCase.Status == DeathCaseStatus.Draft)
        {
            throw ObjectionMapper.CaseNotFound();
        }
        return deathCase;
    }

    private static Guid? FindActiveVerifier(DeathCase deathCase)
    {
        foreach (DeathCaseAssignment assignment in deathCase.Assignments)
        {
            if (assignment.EndedAt == null)
            {
                return assignment.VerifierUserId;
            }
        }
        return null;
    }

    private async Task<Vault> LoadOwnedVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        return VaultRules.EnsureOwnedBy(vault, userId);
    }

    private static async Task<byte[]> ReadImageAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (!EkycRules.IsAllowedContentType(contentType))
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.EkycImageRejected, "Chỉ nhận ảnh JPEG hoặc PNG.");
        }

        using (MemoryStream buffer = new MemoryStream())
        {
            byte[] chunk = new byte[81920];
            int read = await content.ReadAsync(chunk, 0, chunk.Length, ct);
            while (read > 0)
            {
                if (buffer.Length + read > EkycRules.MaxImageBytes)
                {
                    throw new AppException(AppErrorType.PayloadTooLarge, ErrorCode.FileTooLarge, "Ảnh vượt quá 8 MiB.");
                }
                buffer.Write(chunk, 0, read);
                read = await content.ReadAsync(chunk, 0, chunk.Length, ct);
            }
            if (buffer.Length == 0)
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.EkycImageRejected, "Ảnh rỗng.");
            }
            return buffer.ToArray();
        }
    }
}
