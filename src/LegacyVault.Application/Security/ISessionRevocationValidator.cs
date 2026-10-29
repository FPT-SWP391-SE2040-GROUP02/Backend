namespace LegacyVault.Application.Security;

/// <summary>
/// Xác thực và kiểm tra tính hợp lệ của phiên đăng nhập (family) khi nhận Access Token mang claim family_id.
/// </summary>
public interface ISessionRevocationValidator
{
    /// <summary>
    /// Kiểm tra phiên (family) còn tồn tại ít nhất 1 refresh token active (RevokedAt == null) hay không.
    /// </summary>
    Task<bool> IsSessionActiveAsync(Guid familyId, CancellationToken ct = default);

    /// <summary>
    /// Xóa cache trạng thái của familyId ngay khi bị thu hồi (đăng xuất hoặc logout-all).
    /// </summary>
    void InvalidateFamily(Guid familyId);
}
