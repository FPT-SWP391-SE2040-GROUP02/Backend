namespace LegacyVault.Application.Notifications;

/// <summary>Một thư bổ sung đã thay xong biến, sẵn sàng gửi cho Chủ kho.</summary>
public sealed class ExtraMailMessage
{
    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string ButtonText { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Gửi thư bổ sung do Admin đặt (nút dẫn tới trang điểm danh). Gửi lỗi thì ném exception: job không đánh dấu đã gửi và vòng sau thử lại.
/// Nội dung chỉ có chữ Admin viết và tên người nhận; không có nội dung tài sản (OPS-04).
/// </summary>
public interface IExtraMailSender
{
    Task SendAsync(ExtraMailMessage message, CancellationToken ct);
}
