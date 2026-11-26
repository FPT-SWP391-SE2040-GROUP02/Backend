namespace LegacyVault.Application.Notifications;

/// <summary>Thư về biên bản phân chia giữa Chủ tài sản và Người thực thi. Không ném lỗi; gửi hỏng thì trả false.</summary>
public interface IExecutorWillNoticeSender
{
    /// <summary>Báo Người thực thi: Chủ tài sản đã ký phiên bản mới, cần xem và trả lời.</summary>
    Task<bool> SendNewVersionAsync(string executorEmail, string ownerName, int versionNo, CancellationToken ct);

    /// <summary>Báo Chủ tài sản: Người thực thi không đồng ý phiên bản mới.</summary>
    Task<bool> SendDeclinedAsync(string ownerEmail, string executorName, int versionNo, CancellationToken ct);
}
