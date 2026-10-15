using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
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
/// - D3, TIME-02: nhắc Verifier đang giữ hồ sơ UnderReview khi còn không quá 12 giờ tới hạn, mỗi hạn một lần thành công.
///   Hồ sơ SupplementRequired đang chờ Executor thì không nhắc (D2).
/// - SEC-07: không đưa CCCD, tên Owner hay địa chỉ email vào bất kỳ đâu.
/// TODO: thư "bạn được giao" cho Verifier mới và báo Admin khi không có người thay (F4).
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
    private readonly IDeathCaseVerifierReminderSender reminderSender;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public DeathCaseAssignmentJob(
        IDeathCaseRepository deathCaseRepository,
        IDeathCaseAssignmentRepository assignmentRepository,
        IVaultRepository vaultRepository,
        IUserRepository userRepository,
        IDeathCaseVerifierPicker verifierPicker,
        IDeathCaseVerifierReminderSender reminderSender,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.deathCaseRepository = deathCaseRepository;
        this.assignmentRepository = assignmentRepository;
        this.vaultRepository = vaultRepository;
        this.userRepository = userRepository;
        this.verifierPicker = verifierPicker;
        this.reminderSender = reminderSender;
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
        if (reason != null)
        {
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

            // Bước 6 (D5): không có người thay. Verifier đã mất quyền thì không gửi thư cho họ.
            if (reason == DeathCaseAssignmentRules.EndReasonVerifierUnavailable)
            {
                return DeathCaseAssignmentOutcome.NoCandidate;
            }
        }

        // Bước 7 (D3, TIME-02, ASSIGN-04): nhắc Verifier đang giữ hồ sơ, gồm cả cảnh báo quá hạn khi OVERDUE
        // mà không có người thay. Mỗi hạn chỉ nhắc thành công một lần (RemindedDueAt).
        DeathCaseAssignmentOutcome? reminderOutcome = await TryRemindAsync(deathCase, active, holder, now, ct);
        if (reminderOutcome != null)
        {
            return reminderOutcome.Value;
        }

        // Bước 8: OVERDUE không có người thay và đã nhắc cho hạn này thì vẫn báo NoCandidate; còn lại không có việc.
        if (reason != null)
        {
            return DeathCaseAssignmentOutcome.NoCandidate;
        }
        return DeathCaseAssignmentOutcome.None;
    }

    /// <summary>
    /// D3, D2: chỉ nhắc khi hồ sơ UnderReview, đến lúc nhắc cho hạn này và Verifier có email. Trả null khi không có
    /// gì để nhắc. Gửi lỗi → ReminderFailed (không lưu, vòng sau thử lại, không đếm số lần). Gửi được → ghi
    /// RemindedDueAt rồi lưu một lần; không đổi UpdatedAt (D4). Gửi được nhưng lưu lỗi thì có thể gửi trùng một lần.
    /// </summary>
    private async Task<DeathCaseAssignmentOutcome?> TryRemindAsync(
        DeathCase deathCase, DeathCaseAssignment active, User? holder, DateTimeOffset now, CancellationToken ct)
    {
        if (deathCase.Status != DeathCaseStatus.UnderReview)
        {
            return null;
        }
        if (!DeathCaseAssignmentRules.NeedsReminder(active, now))
        {
            return null;
        }
        if (holder == null || string.IsNullOrWhiteSpace(holder.Email))
        {
            return null;
        }

        DeathCaseVerifierReminder reminder = new DeathCaseVerifierReminder();
        reminder.AssignmentId = active.Id;
        reminder.ToEmail = holder.Email;
        reminder.FullName = holder.FullName;
        reminder.CaseCode = DeathCaseRules.CaseCode(deathCase.Id);
        reminder.DueAt = active.DueAt;
        try
        {
            await reminderSender.SendAsync(reminder, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Không ghi chi tiết lỗi (có thể chứa địa chỉ email); RemindedDueAt vẫn cũ nên vòng sau thử lại.
            return DeathCaseAssignmentOutcome.ReminderFailed;
        }

        active.RemindedDueAt = active.DueAt;
        bool saved = await TrySaveAsync(ct);
        if (!saved)
        {
            return DeathCaseAssignmentOutcome.Conflict;
        }
        return DeathCaseAssignmentOutcome.Reminded;
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
