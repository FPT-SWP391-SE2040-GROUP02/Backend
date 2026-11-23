namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một phiên bản biên bản phân chia đã được Chủ kho ký bằng PIN (bảng WillVersions).
/// </summary>
/// <remarks>
/// - Phiên bản 1 sinh khi kích hoạt kế hoạch; mỗi lần Chủ kho ký lại sau khi đổi cấu hình sinh phiên bản kế tiếp.
/// - SnapshotJson chỉ chứa mã định danh (cặp gói-người nhận và cặp gói-tài sản), không có tên tài sản,
///   tên người nhận hay nội dung nhạy cảm (SEC-07).
/// - Khi bàn giao chỉ phần thuộc bản ký gần nhất VÀ còn tồn tại trong cấu hình hiện tại mới được bàn giao.
/// </remarks>
public class WillVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    /// <summary>Số thứ tự phiên bản trong kho, bắt đầu từ 1, duy nhất theo (VaultId, VersionNo).</summary>
    public int VersionNo { get; set; }

    /// <summary>HandoverPlan.ConfigVersion tại lúc ký.</summary>
    public int ConfigVersion { get; set; }

    public DateTimeOffset SignedAt { get; set; }

    /// <summary>JSON chỉ gồm id: {"assignments":[{assetId,beneficiaryId}]}.</summary>
    public string SnapshotJson { get; set; } = string.Empty;
}
