namespace LegacyVault.Domain.Enums;

/// <summary>Trạng thái lưu dữ liệu của một lượt bàn giao (SRS §7.7, §11.1 "Lưu dữ liệu").</summary>
/// <remarks>DELETION_ELIGIBLE của SRS chỉ là bước chuyển trong job (tới hạn và kiểm lại đủ điều kiện thì xóa ngay), không lưu.</remarks>
public enum RetentionStatus
{
    /// <summary>Chưa đủ điều kiện bắt đầu đồng hồ (còn trong hạn phản hồi, claim đang xử lý, grant còn hạn...).</summary>
    NotStarted = 0,
    /// <summary>Đồng hồ 30 ngày đang chạy.</summary>
    RetentionActive = 1,
    /// <summary>Tạm dừng vì có lý do giữ; thời gian còn lại lưu ở RetentionRemainingSeconds, không đặt lại 30 ngày.</summary>
    DeletionHeld = 2,
    /// <summary>Đã dọn: vô hiệu link, xóa nội dung không còn ai giữ, ghi dấu đã xóa.</summary>
    Deleted = 3,
}
