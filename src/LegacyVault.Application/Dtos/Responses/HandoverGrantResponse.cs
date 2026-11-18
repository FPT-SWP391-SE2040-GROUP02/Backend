namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Quyền xem và tải của người nhận: trạng thái (đã tính cả trường hợp quá hạn) và hạn.</summary>
public class HandoverGrantResponse
{
    public Guid Id { get; set; }

    /// <summary>ACTIVE, BLOCKED, EXPIRED, REVOKED (đã tính cả trường hợp quá hạn mà job chưa chạy).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}
