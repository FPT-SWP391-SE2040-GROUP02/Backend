using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng PasswordResetTokens.</summary>
public interface IPasswordResetTokenRepository
{
    /// <summary>Tìm theo mã băm, kèm User. Không thấy thì trả null.</summary>
    Task<PasswordResetToken?> FindByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>Các link chưa dùng của tài khoản, mới nhất trước (để vô hiệu khi có link mới hoặc đã đặt lại).</summary>
    Task<List<PasswordResetToken>> FindUnusedByUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Đánh dấu cần INSERT; ghi DB khi IUnitOfWork.SaveChangesAsync.</summary>
    void Add(PasswordResetToken token);
}
