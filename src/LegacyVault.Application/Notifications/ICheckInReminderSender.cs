using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Notifications;

/// <summary>
/// Gửi lời nhắc điểm danh cho Owner (DMS-06). Cài đặt ở Infrastructure (email/SMS).
/// </summary>
/// <remarks>
/// OPS-04: nội dung chỉ gồm tên và các mốc thời gian; không chứa nội dung tài sản.
/// Gửi lỗi thì ném exception: job giữ lời nhắc ở trạng thái chờ và thử lại ở vòng sau.
/// </remarks>
public interface ICheckInReminderSender
{
    Task SendReminderAsync(
        string toEmail, string fullName, CheckInReminderKind kind,
        DateTimeOffset dueAt, DateTimeOffset graceEndsAt, CancellationToken ct);
}
