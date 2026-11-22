using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một chữ ký trên biên bản bàn giao (bảng HandoverRecordSignatures). "Ký" là bấm xác nhận, lưu người ký và thời điểm.
/// </summary>
/// <remarks>Chỉ chữ ký có RevisionNo bằng RevisionNo hiện tại của biên bản mới tính.</remarks>
public class HandoverRecordSignature
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RecordId { get; set; }
    public HandoverRecord Record { get; set; } = null!;
    public int RevisionNo { get; set; }
    public HandoverSignerRole Role { get; set; }
    public Guid SignerUserId { get; set; }

    /// <summary>Chỉ với người nhận: hồ sơ người nhận đã ký.</summary>
    public Guid? BeneficiaryId { get; set; }

    public DateTimeOffset SignedAt { get; set; }
}
