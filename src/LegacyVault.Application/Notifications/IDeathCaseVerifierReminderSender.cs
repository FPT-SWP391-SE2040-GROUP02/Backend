namespace LegacyVault.Application.Notifications;

/// <summary>Dữ liệu một thư nhắc Verifier sắp quá hạn xét hồ sơ chứng tử (ASSIGN-04, TIME-02, D3).</summary>
/// <remarks>
/// OPS-04, SEC-07: KHÔNG có CCCD, tên Owner, tên Executor hay nội dung hồ sơ; chỉ mã hồ sơ rút gọn và hạn xét.
/// </remarks>
public sealed class DeathCaseVerifierReminder
{
    public Guid AssignmentId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    /// <summary>Họ tên tài khoản Verifier (lấy từ tài khoản, không lấy từ hồ sơ chứng tử).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Mã hồ sơ rút gọn để Verifier đối chiếu khi đăng nhập.</summary>
    public string CaseCode { get; set; } = string.Empty;

    /// <summary>Hạn Verifier phải ra quyết định (D1).</summary>
    public DateTimeOffset DueAt { get; set; }
}

/// <summary>
/// Gửi thư nhắc Verifier trước hạn xét 12 giờ (D3). Cài đặt ở Infrastructure (email).
/// </summary>
/// <remarks>
/// Trả về bình thường = máy chủ SMTP đã nhận thư (coi là "đã nhắc"). Gửi lỗi thì ném exception;
/// DeathCaseAssignmentJob không lưu gì và vòng sau thử lại.
/// </remarks>
public interface IDeathCaseVerifierReminderSender
{
    Task SendAsync(DeathCaseVerifierReminder reminder, CancellationToken ct);
}
