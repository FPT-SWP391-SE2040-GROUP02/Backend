namespace LegacyVault.Application.Notifications;

/// <summary>Đọc họ tên Chủ tài sản và liên hệ Người thực thi của kho. Chỉ đọc; không có nội dung tài sản.</summary>
public interface IHandoverContactReader
{
    /// <summary>Kho không có Chủ tài sản hoặc chưa có Người thực thi đang hiệu lực thì trường tương ứng để rỗng.</summary>
    Task<HandoverContactInfo> FindAsync(Guid vaultId, CancellationToken ct);
}
