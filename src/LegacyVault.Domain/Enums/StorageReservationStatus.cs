namespace LegacyVault.Domain.Enums;

/// <summary>Trạng thái một chỗ giữ dung lượng cho lần tải lên đang diễn ra (QUOTA-02).</summary>
public enum StorageReservationStatus
{
    /// <summary>Đang giữ chỗ; hết ExpiresAt thì không còn tính (tự giải phóng).</summary>
    Active = 0,
    /// <summary>Tải lên xong, tài sản đã ghi; dung lượng giờ tính qua phiên bản tài sản.</summary>
    Committed = 1,
    /// <summary>Tải lên lỗi hoặc bị hủy; đã trả chỗ.</summary>
    Released = 2,
}
