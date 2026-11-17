namespace LegacyVault.Application.Notifications;

/// <summary>Dữ liệu một thư báo Executor rằng Verifier vừa yêu cầu bổ sung hồ sơ chứng tử.</summary>
/// <remarks>
/// Không có CCCD, tên Owner, lý do Verifier ghi hay nội dung hồ sơ: lý do có thể chứa dữ liệu cá nhân,
/// Executor đọc lý do sau khi đăng nhập.
/// </remarks>
public sealed class DeathCaseExecutorNotice
{
    public Guid CaseId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    /// <summary>Họ tên tài khoản Executor (lấy từ tài khoản, không lấy từ hồ sơ chứng tử).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Mã hồ sơ rút gọn để Executor đối chiếu khi đăng nhập.</summary>
    public string CaseCode { get; set; } = string.Empty;

    /// <summary>Hạn nộp bản bổ sung.</summary>
    public DateTimeOffset? SupplementDueAt { get; set; }
}

/// <summary>
/// Gửi thư báo Executor khi Verifier yêu cầu bổ sung hồ sơ. Cài đặt ở Infrastructure (email).
/// </summary>
/// <remarks>
/// Thư gửi sau khi quyết định đã lưu nên KHÔNG được ném lỗi gửi thư: quyết định của Verifier không phụ thuộc
/// việc gửi thư; bộ gửi tự ghi log khi lỗi. Executor vẫn thấy yêu cầu trong ứng dụng.
/// </remarks>
public interface IDeathCaseExecutorNoticeSender
{
    Task SendAsync(DeathCaseExecutorNotice notice, CancellationToken ct);
}
