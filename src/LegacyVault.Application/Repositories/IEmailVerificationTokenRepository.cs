using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng EmailVerificationTokens (AUTH-04).</summary>
public interface IEmailVerificationTokenRepository
{
    /// <summary>Tìm theo hash, kèm User. Không thấy → null.</summary>
    Task<EmailVerificationToken?> FindByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>Các link chưa dùng của user (để vô hiệu khi gửi lại link mới).</summary>
    Task<List<EmailVerificationToken>> FindUnusedByUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT"; ghi DB khi IUnitOfWork.SaveChangesAsync.</summary>
    void Add(EmailVerificationToken token);
}
