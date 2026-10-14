using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Quyền xem và tải của người nhận sau khi đồng ý nhận (bảng HandoverGrants), SRS §7.5, §7.6.
/// </summary>
/// <remarks>
/// - Unique theo TurnId: một lượt chỉ có một grant (DATA-04), nên bấm nhận nhiều lần hay song song vẫn một kết quả.
/// - ExpiresAt = lúc nhận + 30 ngày, không gia hạn (DOWNLOAD-02). Hạn được kiểm lại ở MỖI request, không chờ job.
/// </remarks>
public class HandoverGrant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TurnId { get; set; }

    public HandoverTurn Turn { get; set; } = null!;

    public Guid UserId { get; set; }

    public HandoverGrantStatus Status { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
