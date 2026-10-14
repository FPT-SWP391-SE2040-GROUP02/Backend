namespace LegacyVault.Application.Notifications;

/// <summary>Dữ liệu một thông báo hồ sơ chứng tử gửi cho Owner (DEATH-08).</summary>
/// <remarks>
/// OPS-04, SEC-07: KHÔNG có CCCD, tên Executor, số giấy hay nội dung khai báo; chỉ mã hồ sơ rút gọn và hạn phản đối.
/// </remarks>
public sealed class DeathCaseOwnerNotice
{
    public Guid NoticeId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    /// <summary>Họ tên tài khoản Owner (lấy từ tài khoản, không lấy từ hồ sơ chứng tử).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Mã hồ sơ rút gọn để Owner đối chiếu khi đăng nhập.</summary>
    public string CaseCode { get; set; } = string.Empty;

    /// <summary>Hạn phản đối dự kiến nếu thư được giao ngay lúc gửi (DEATH-08).</summary>
    public DateTimeOffset ExpectedObjectionEndsAt { get; set; }
}

/// <summary>
/// Gửi thông báo cho Owner khi có hồ sơ chứng tử (DEATH-08, D3). Cài đặt ở Infrastructure (email).
/// </summary>
/// <remarks>
/// Trả về bình thường = máy chủ SMTP đã nhận thư (coi là "đã giao"). Gửi lỗi thì ném exception;
/// DeathCaseNoticeJob tăng số lần thử và giữ thông báo Pending.
/// </remarks>
public interface IDeathCaseNoticeSender
{
    Task SendAsync(DeathCaseOwnerNotice notice, CancellationToken ct);
}
