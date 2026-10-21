using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập UserTotps, TwoFactorRecoveryCodes và TwoFactorChallenges.</summary>
public interface ITwoFactorRepository
{
    /// <summary>Khóa TOTP của user (có theo dõi thay đổi). Chưa có → null.</summary>
    Task<UserTotp?> FindTotpAsync(Guid userId, CancellationToken ct);

    void AddTotp(UserTotp totp);

    void RemoveTotp(UserTotp totp);

    /// <summary>Mã khôi phục chưa dùng của user.</summary>
    Task<List<TwoFactorRecoveryCode>> FindUnusedRecoveryCodesAsync(Guid userId, CancellationToken ct);

    /// <summary>Mọi mã khôi phục (đã dùng hoặc chưa) để xóa khi tắt/tạo lại.</summary>
    Task<List<TwoFactorRecoveryCode>> FindAllRecoveryCodesAsync(Guid userId, CancellationToken ct);

    void AddRecoveryCode(TwoFactorRecoveryCode code);

    void RemoveRecoveryCode(TwoFactorRecoveryCode code);

    /// <summary>Thử thách theo mã băm, kèm User. Không thấy → null.</summary>
    Task<TwoFactorChallenge?> FindChallengeByHashAsync(string tokenHash, CancellationToken ct);

    void AddChallenge(TwoFactorChallenge challenge);
}
