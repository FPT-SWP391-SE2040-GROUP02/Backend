using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng ExternalLogins (liên kết Google ↔ User).</summary>
public interface IExternalLoginRepository
{
    /// <summary>Liên kết theo sub của nhà cung cấp, kèm User. Chưa liên kết → null.</summary>
    Task<ExternalLogin?> FindBySubjectAsync(LoginProvider provider, string subject, CancellationToken ct);

    /// <summary>Liên kết của một user với một nhà cung cấp. Chưa có → null.</summary>
    Task<ExternalLogin?> FindByUserAsync(Guid userId, LoginProvider provider, CancellationToken ct);

    /// <summary>Mọi liên kết của một user (màn hình "Phương thức đăng nhập").</summary>
    Task<List<ExternalLogin>> FindAllByUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Thêm liên kết rồi lưu ngay (cùng các thay đổi đang chờ khác của request).
    /// Trả false khi DB báo trùng (Provider, ProviderSubject) hoặc (UserId, Provider): request khác vừa liên kết trước.
    /// </summary>
    Task<bool> TryAddAsync(ExternalLogin login, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ DELETE".</summary>
    void Remove(ExternalLogin login);
}
