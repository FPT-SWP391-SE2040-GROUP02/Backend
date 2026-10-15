using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật thuần về phân công Verifier cho hồ sơ chứng tử (SRS §6.4 ASSIGN-01..04, §10.2 TIME-01, TIME-07),
/// dùng chung cho service gửi hồ sơ, job báo Owner và job phân công.
/// </summary>
public static class DeathCaseAssignmentRules
{
    /// <summary>D3, TIME-02: nhắc Verifier khi còn không quá 12 giờ tới hạn.</summary>
    public const int ReminderBeforeHours = 12;

    /// <summary>D5, ASSIGN-04: giao lại vì Verifier giữ hồ sơ quá hạn.</summary>
    public const string EndReasonOverdue = "OVERDUE";

    /// <summary>D5, ASSIGN-02, ADMIN-05: giao lại vì Verifier không còn Active hoặc không còn vai trò Verifier.</summary>
    public const string EndReasonVerifierUnavailable = "VERIFIER_UNAVAILABLE";

    /// <summary>
    /// D1, ASSIGN-04, TIME-07: hạn Verifier phải ra quyết định = mốc muộn hơn giữa (start + 48 giờ)
    /// và (hết phản đối + 48 giờ). start là lúc giao hoặc lúc Executor gửi bản mới gần nhất.
    /// Các đồng hồ không đặt lại hay tạm dừng nhau; hạn Verifier chỉ có mức sàn "sau khi hồ sơ duyệt được".
    /// </summary>
    public static DateTimeOffset ComputeDueAt(DateTimeOffset start, DateTimeOffset? objectionEndsAt)
    {
        DateTimeOffset dueAt = start.AddHours(DeathCaseRules.ReviewDueHours);
        if (objectionEndsAt != null)
        {
            DateTimeOffset afterObjection = objectionEndsAt.Value.AddHours(DeathCaseRules.ReviewDueHours);
            if (afterObjection > dueAt)
            {
                dueAt = afterObjection;
            }
        }
        return dueAt;
    }

    /// <summary>
    /// D1, TIME-07: khi Owner nhận thư và đồng hồ 72 giờ bắt đầu, dời hạn của phân công đang hiệu lực
    /// nếu hạn hiện tại sớm hơn (hết phản đối + 48 giờ). Không bao giờ rút ngắn hạn.
    /// </summary>
    public static DateTimeOffset ExtendForObjection(DateTimeOffset dueAt, DateTimeOffset objectionEndsAt)
    {
        DateTimeOffset afterObjection = objectionEndsAt.AddHours(DeathCaseRules.ReviewDueHours);
        if (afterObjection > dueAt)
        {
            return afterObjection;
        }
        return dueAt;
    }

    /// <summary>TIME-01: đúng lúc hết hạn là quá hạn.</summary>
    public static bool IsOverdue(DateTimeOffset dueAt, DateTimeOffset now)
    {
        return now >= dueAt;
    }

    /// <summary>
    /// D3, TIME-02: cần nhắc khi chỉ còn không quá 12 giờ tới hạn và chưa nhắc thành công cho đúng hạn này.
    /// Hạn bị dời (D1, Executor gửi lại) thì khác RemindedDueAt nên được nhắc lại cho hạn mới.
    /// Nơi gọi tự kiểm trạng thái hồ sơ (D2: chỉ nhắc khi UnderReview).
    /// </summary>
    public static bool NeedsReminder(DeathCaseAssignment assignment, DateTimeOffset now)
    {
        DateTimeOffset remindFrom = assignment.DueAt.AddHours(-ReminderBeforeHours);
        if (now < remindFrom)
        {
            return false;
        }
        if (assignment.RemindedDueAt == null)
        {
            return true;
        }
        if (assignment.RemindedDueAt.Value != assignment.DueAt)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// D5, ASSIGN-02, ADMIN-05: Verifier còn dùng được khi tài khoản tồn tại, đang Active
    /// và vẫn giữ vai trò Verifier.
    /// </summary>
    public static bool IsVerifierAvailable(User? user)
    {
        if (user == null)
        {
            return false;
        }
        if (user.Status != UserStatus.Active)
        {
            return false;
        }
        if (user.Role != UserRole.Verifier)
        {
            return false;
        }
        return true;
    }
}
