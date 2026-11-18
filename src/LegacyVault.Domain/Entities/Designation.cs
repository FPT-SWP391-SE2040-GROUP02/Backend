namespace LegacyVault.Domain.Entities;

/// <summary>
/// Chỉ định một hồ sơ người nhận cho một gói (bảng Designations), SRS §5.2 bước 7.
/// </summary>
/// <remarks>
/// - SETUP-07: chỉ định có ID nội bộ riêng, không gắn theo email hay tên. Người nhận không có tài khoản gắn vào chỉ định.
/// - Mỗi hồ sơ có tối đa một chỉ định trong một gói (unique ContainerId + BeneficiaryId).
/// - QUOTA-03: suất tính theo số NGƯỜI (BeneficiaryId) khác nhau có chỉ định trong kho, không theo số dòng.
/// - Gói đã xóa không còn chỉ định nào (SETUP-16).
/// - Snapshot (M10) sẽ chép chỉ định lúc ghim; thay đổi về sau không làm đổi snapshot (SETUP-05).
/// </remarks>
public class Designation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ContainerId { get; set; }

    public Container Container { get; set; } = null!;

    public Guid BeneficiaryId { get; set; }

    public Beneficiary Beneficiary { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
