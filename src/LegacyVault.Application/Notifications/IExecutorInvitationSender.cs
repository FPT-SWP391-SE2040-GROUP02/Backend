namespace LegacyVault.Application.Notifications;

/// <summary>Returns true only after delivery acknowledgement. Never logs a token or destination.</summary>
public interface IExecutorInvitationSender
{
    Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>
    /// Lời mời do quản trị viên giao khi kho kẹt, lời thư không nói "Chủ kho mời". Mặc định gửi như lời mời thường để các
    /// bản cài đặt cũ vẫn chạy; bản gửi thật ghi đè bằng lời của quản trị viên.
    /// </summary>
    Task<bool> SendAssignedByAdminAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
    {
        return SendAsync(email, token, expiresAt, ct);
    }

    /// <summary>
    /// Lời mời do Chủ kho gửi: thư nêu rõ tên Chủ kho để người nhận biết ai mời. Mặc định gửi như lời mời thường để các
    /// bản cài đặt cũ vẫn chạy; bản gửi thật ghi đè bằng lời thư có tên Chủ kho.
    /// </summary>
    Task<bool> SendFromOwnerAsync(string email, string ownerName, string token, DateTimeOffset expiresAt, CancellationToken ct)
    {
        return SendAsync(email, token, expiresAt, ct);
    }
}
