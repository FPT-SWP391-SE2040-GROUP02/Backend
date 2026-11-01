namespace LegacyVault.Application.Repositories;

/// <summary>Truy vấn riêng của job hết hạn dịch vụ (OPLAN-01).</summary>
public interface IEntitlementExpiryRepository
{
    /// <summary>
    /// Id các kho có quyền dịch vụ PAID_ACTIVE đã tới hoặc qua hạn lúc <paramref name="now"/>, cũ nhất trước,
    /// tối đa <paramref name="max"/> kho mỗi lần quét.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindExpiredPaidVaultIdsAsync(DateTimeOffset now, int max, CancellationToken ct);
}
