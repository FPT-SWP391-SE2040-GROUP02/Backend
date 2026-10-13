using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng RefreshTokens.</summary>
public interface IRefreshTokenRepository
{
    /// <summary>Tìm theo hash, kèm User. Không thấy → null.</summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>Các token CHƯA thu hồi của một family (một lần đăng nhập trên một thiết bị).</summary>
    Task<List<RefreshToken>> FindActiveByFamilyAsync(Guid familyId, CancellationToken ct);

    /// <summary>Các token CHƯA thu hồi của một user (mọi thiết bị) — đăng xuất mọi nơi, khóa tài khoản (ROLE-04).</summary>
    Task<List<RefreshToken>> FindActiveByUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT"; ghi DB khi IUnitOfWork.SaveChangesAsync.</summary>
    void Add(RefreshToken token);
}
