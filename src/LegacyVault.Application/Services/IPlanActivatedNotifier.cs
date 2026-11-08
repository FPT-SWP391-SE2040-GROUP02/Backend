namespace LegacyVault.Application.Services;

/// <summary>Báo cho từng người nhận của kho rằng họ đã được chọn, ngay sau khi kế hoạch được kích hoạt.</summary>
public interface IPlanActivatedNotifier
{
    /// <summary>
    /// Gửi một thư cho mỗi người nhận có trong gói đã sẵn sàng, kèm thông tin liên hệ của Chủ kho và Người thực thi.
    /// Không ném lỗi: thư nào gửi lỗi thì bỏ qua thư đó, việc kích hoạt vẫn giữ nguyên.
    /// </summary>
    Task NotifyAsync(Guid vaultId, CancellationToken ct);
}
