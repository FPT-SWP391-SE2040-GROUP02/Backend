using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// - ALIVE-01: báo lần đầu chặn ngay trong MỘT giao dịch: hồ sơ sang Suspended, kế hoạch sang Blocked (chặn nhận lượt,
///   đồng ý, tải), grant đang có sang Blocked. HandoverService ghi plan.UpdatedAt khi nhận/đồng ý nên thao tác nhận
///   chạy cùng lúc với báo sẽ thua RowVersion của kế hoạch (AC-34).
/// - ALIVE-09: báo lặp chỉ tăng bộ đếm, không đổi hạn, không tạo hold trùng (chỉ mục unique có lọc).
/// - Selfie so với mẫu khuôn mặt qua IOwnerFaceVerifier; lỗi kỹ thuật không tính là thất bại (EKYC-19).
/// - TODO(LV-07): tạo sự cố theo dõi, báo Verifier/Executor bằng thông báo.
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
        TimeProvider timeProvider)
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
            // ALIVE-09: sau khi bị bác, muốn mở lại phải có bằng chứng mới (chưa có đường nộp bằng chứng mới).
            throw ObjectionMapper.NotAllowed(DeathCaseNotAllowedReason.ObjectionAlreadyRejected);
        }
        if (!ObjectionRules.CanReport(deathCase.Status))
        {
            throw ObjectionMapper.CaseNotFound();
        }

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
        objections.Add(objection);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (objections.IsUniqueViolation(ex))
        {
            throw ObjectionMapper.EditConflict();
        }
        return await BuildAsync(deathCase, ct);
    }

    public async Task<OwnerDeathCaseResponse> SubmitSelfieAsync(
        Guid vaultId, Stream content, string fileName, string contentType, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        DeathCase deathCase = await LoadSubmittedCaseAsync(vault.Id, ct);
        DeathCaseObjection? objection = await objections.FindOpenByCaseAsync(deathCase.Id, ct);
        if (objection == null)
        {
            throw ObjectionMapper.CaseNotFound();
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
        if (outcome == OwnerFaceOutcome.Unavailable)
        {
            throw new AppException(AppErrorType.ServiceUnavailable, ErrorCode.EkycServiceUnavailable,
                "Chưa so khớp được khuôn mặt lúc này. Vui lòng thử lại sau.");
        }

        if (outcome == OwnerFaceOutcome.Matched)
        {
            objection.SelfiePassedAt = now;
        }
        else
        {
            ObjectionRules.RecordFailure(objection, now);
        }
        objection.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
        return await BuildAsync(deathCase, ct);
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
