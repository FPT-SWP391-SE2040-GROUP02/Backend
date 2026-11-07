namespace LegacyVault.Application.Notifications;

/// <summary>
/// Gửi thư báo cho người nhận khi Chủ kho kích hoạt kế hoạch. Cài đặt ở Infrastructure (email).
/// Gửi lỗi thì ném exception; nơi gọi tự quyết định có bỏ qua hay không.
/// </summary>
public interface IPlanActivatedNoticeSender
{
    Task SendAsync(PlanActivatedNotice notice, CancellationToken ct);
}
