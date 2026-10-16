namespace LegacyVault.Application.Repositories;

/// <summary>Đọc danh tính đã xác minh của Owner để đối chiếu hồ sơ chứng tử (DEATH-13, EKYC-27).</summary>
public interface IOwnerIdentityReader
{
    /// <summary>
    /// 4 số cuối CCCD của lượt eKYC đạt gần nhất của tài khoản; chưa có lượt đạt → null.
    /// TODO(LV-02): thay bằng danh tính đã gắn (CCCD đủ 12 số) khi có.
    /// </summary>
    Task<string?> FindVerifiedCitizenIdLast4Async(Guid userId, CancellationToken ct);
}
