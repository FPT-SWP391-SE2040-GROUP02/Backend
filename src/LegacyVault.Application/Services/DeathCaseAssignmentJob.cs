using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// - ASSIGN-04: hồ sơ luôn có người phụ trách và hạn. Chưa có người thì giao; quá hạn hoặc Verifier mất quyền thì giao lại.
/// - D5: chỉ kết thúc phân công cũ khi tìm được người thay; không có ai thì giữ nguyên, vòng sau thử lại.
/// - D4: giao lại đổi DeathCase.UpdatedAt (etag) để Verifier cũ đang thao tác nhận 409 EDIT_CONFLICT. Giao người cho hồ sơ
///   trống thì không đổi UpdatedAt vì không ai mất quyền.
/// - Job KHÔNG BAO GIỜ đổi trạng thái hồ sơ, thêm quyết định hay mở bàn giao: quá hạn không tự cho đạt (ASSIGN-04).
/// - ASSIGN-03: không sửa hay xóa quyết định cũ; quyết định cũ vẫn gắn AssignmentId của phân công đã kết thúc.
/// - Mỗi hồ sơ SaveChanges tối đa một lần; đóng phân công cũ và thêm phân công mới nằm trong cùng lần lưu.
/// - SEC-07: không đưa CCCD, tên Owner hay địa chỉ email vào bất kỳ đâu.
/// TODO: nhắc Verifier trước hạn 12 giờ (D3) ở Bước 4; thư "bạn được giao" và báo Admin khi không có người thay (F4).
/// </remarks>
public sealed class DeathCaseAssignmentJob : IDeathCaseAssignmentJob
{
    /// <summary>Số hồ sơ tối đa xử lý mỗi vòng quét.</summary>
    private const int BatchSize = 50;

    private readonly IDeathCaseRepository deathCaseRepository;
    private readonly IDeathCaseAssignmentRepository assignmentRepository;
    private readonly IVaultRepository vaultRepository;
    private readonly IUserRepository userRepository;
    private readonly IDeathCaseVerifierPicker verifierPicker;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public DeathCaseAssignmentJob(
        IDeathCaseRepository deathCaseRepository,
        IDeathCaseAssignmentRepository assignmentRepository,
        IVaultRepository vaultRepository,
        IUserRepository userRepository,
        IDeathCaseVerifierPicker verifierPicker,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.deathCaseRepository = deathCaseRepository;
        this.assignmentRepository = assignmentRepository;
        this.vaultRepository = vaultRepository;
        this.userRepository = userRepository;
        this.verifierPicker = verifierPicker;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<List<Guid>> FindCaseIdsAsync(CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return assignmentRepository.FindCaseIdsForJobAsync(now, BatchSize, ct);
    }

    public async Task<DeathCaseAssignmentOutcome> ProcessCaseAsync(Guid caseId, CancellationToken ct)
    {
        // Bước 1: đọc lại và kiểm lại trạng thái (TIME-02: chạy lặp an toàn). Hồ sơ đã đóng hoặc còn Draft thì bỏ qua.
        DeathCase? deathCase = await deathCaseRepository.FindByIdAsync(caseId, ct);
        if (deathCase == null || !DeathCaseReviewRules.CanDecide(deathCase.Status))
        {
            return DeathCaseAssignmentOutcome.None;
        }

        // Bước 2: phân công đang hiệu lực (FindByIdAsync chỉ nạp dòng EndedAt null, tối đa một), kho và người gửi bản đang xét.
        DeathCaseAssignment? active = null;
        foreach (DeathCaseAssignment assignment in deathCase.Assignments)
        {
            if (assignment.EndedAt == null)
            {
                active = assignment;
            }
        }
        Vault? vault = await vaultRepository.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null)
        {
            return DeathCaseAssignmentOutcome.None;
        }
        DeathCaseVersion reviewVersion = DeathCaseReviewRules.FindReviewVersion(deathCase);
        if (reviewVersion.SubmittedByUserId == null)
        {
            throw new InvalidOperationException("Hồ sơ chứng tử đang xét nhưng bản xét chưa có người gửi.");
        }
        Guid submitterUserId = reviewVersion.SubmittedByUserId.Value;
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Bước 3 (D2, ASSIGN-04): chưa có người giữ thì giao, cả UnderReview lẫn SupplementRequired.
        // Không đổi UpdatedAt (D4): không ai mất quyền, giao trùng đã có unique UX_DeathCaseAssignments_ActivePerCase chặn.
        if (active == null)
        {
            Guid? firstPick = await verifierPicker.PickAsync(vault, submitterUserId, null, ct);
            if (firstPick == null)
            {
                return DeathCaseAssignmentOutcome.NoCandidate;
            }
            AddAssignment(deathCase, firstPick.Value, now);
            bool assignedSaved = await TrySaveAsync(ct);
            if (!assignedSaved)
            {
                return DeathCaseAssignmentOutcome.Conflict;
            }
            return DeathCaseAssignmentOutcome.Assigned;
        }

        // Bước 4 (D5): lý do giao lại. Verifier mất quyền thì giao ngay; quá hạn chỉ tính khi UnderReview (D2).
        User? holder = await userRepository.FindByIdAsync(active.VerifierUserId, ct);
        string? reason = null;
        if (!DeathCaseAssignmentRules.IsVerifierAvailable(holder))
        {
            reason = DeathCaseAssignmentRules.EndReasonVerifierUnavailable;
        }
        else if (deathCase.Status == DeathCaseStatus.UnderReview
            && DeathCaseAssignmentRules.IsOverdue(active.DueAt, now))
        {
            reason = DeathCaseAssignmentRules.EndReasonOverdue;
        }
        if (reason == null)
        {
            // TODO: Bước 4 — nhắc Verifier trước hạn 12 giờ (D3, TIME-02).
            return DeathCaseAssignmentOutcome.None;
        }

        // Bước 5 (D5): chỉ kết thúc phân công cũ khi tìm được người thay (loại cả Verifier đang giữ).
        Guid? replacement = await verifierPicker.PickAsync(vault, submitterUserId, active.VerifierUserId, ct);
        if (replacement != null)
        {
            active.EndedAt = now;
            active.EndReason = reason;
            AddAssignment(deathCase, replacement.Value, now);
            // D4: đổi etag để Verifier cũ đang thao tác nhận 409 EDIT_CONFLICT, tải lại thì 404.
            deathCase.UpdatedAt = now;
            bool reassignedSaved = await TrySaveAsync(ct);
            if (!reassignedSaved)
            {
                return DeathCaseAssignmentOutcome.Conflict;
            }
            return DeathCaseAssignmentOutcome.Reassigned;
        }

        // Bước 6 và 8: không có người thay. Chưa có thư nhắc ở bước này nên mọi trường hợp đều là NoCandidate.
        // TODO: Bước 4 — OVERDUE không có người thay vẫn nhắc người đang giữ (cảnh báo quá hạn, ASSIGN-04).
        return DeathCaseAssignmentOutcome.NoCandidate;
    }

    /// <summary>ASSIGN-04, D1: thêm phân công mới, hạn = ComputeDueAt(lúc giao, ObjectionEndsAt).</summary>
    private void AddAssignment(DeathCase deathCase, Guid verifierUserId, DateTimeOffset now)
    {
        DeathCaseAssignment assignment = new DeathCaseAssignment();
        assignment.CaseId = deathCase.Id;
        assignment.VerifierUserId = verifierUserId;
        assignment.AssignedAt = now;
        assignment.DueAt = DeathCaseAssignmentRules.ComputeDueAt(now, deathCase.ObjectionEndsAt);
        deathCase.Assignments.Add(assignment);
        deathCaseRepository.AddAssignment(assignment);
    }

    /// <summary>
    /// D4: lưu một lần. Xung đột phiên bản (Verifier hoặc Executor vừa sửa hồ sơ) hoặc trùng phân công đang hiệu lực
    /// thì bỏ mọi thay đổi chưa lưu và trả false; vòng sau đọc lại. Lỗi khác ném lên monitor.
    /// </summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (IsSaveRace(ex))
        {
            deathCaseRepository.DiscardPendingChanges();
            return false;
        }
    }

    private bool IsSaveRace(Exception exception)
    {
        AppException? appException = exception as AppException;
        if (appException != null && appException.Code == ErrorCode.EditConflict)
        {
            return true;
        }
        return deathCaseRepository.IsUniqueViolation(exception);
    }
}
