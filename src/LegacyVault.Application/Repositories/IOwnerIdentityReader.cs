namespace LegacyVault.Application.Repositories;

/// <summary>Đọc danh tính đã xác minh của Owner để đối chiếu hồ sơ chứng tử (DEATH-13, EKYC-27).</summary>
public interface IOwnerIdentityReader
{
    /// <summary>
    /// 4 số cuối CCCD của lượt eKYC đạt gần nhất của tài khoản; chưa có lượt đạt → null.
    /// Chỉ dùng khi Owner chưa có danh tính đã gắn (đăng ký trước khi có danh tính đã gắn).
    /// </summary>
    Task<string?> FindVerifiedCitizenIdLast4Async(Guid userId, CancellationToken ct);

    /// <summary>
    /// Số CCCD đủ 12 số trong danh tính đã gắn của Owner (EKYC-27); chưa gắn → null.
    /// Dữ liệu bị sửa hoặc thiếu KEK → CryptographicException.
    /// </summary>
    Task<string?> FindBoundCitizenIdAsync(Guid userId, CancellationToken ct);
}
