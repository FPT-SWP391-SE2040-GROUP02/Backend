using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Đọc nhanh trạng thái tài khoản cho bước kiểm token ở mỗi request.</summary>
/// <remarks>
/// Access token sống 15 phút. Kiểm lại trong DB ở mỗi request để tài khoản bị khóa hoặc vô hiệu hóa
/// bị chặn ngay, không phải chờ token hết hạn.
/// </remarks>
public interface IUserStatusReader
{
    /// <summary>Trạng thái hiện tại của tài khoản; null khi không còn tài khoản. Chỉ đọc, không theo dõi thay đổi.</summary>
    Task<UserStatus?> FindStatusAsync(Guid userId, CancellationToken ct);
}
