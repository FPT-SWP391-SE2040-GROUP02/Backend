namespace LegacyVault.Domain.Enums;

/// <summary>
/// Trạng thái kế hoạch bàn giao. Giá trị lưu trong DB là tên enum.
/// </summary>
/// <remarks>Không còn điểm danh: kho chỉ chuyển sang bàn giao khi Người thực thi bấm mở bàn giao.</remarks>
public enum PlanStatus
{
    /// <summary>Đang thiết lập.</summary>
    Draft = 0,

    /// <summary>Đã kích hoạt: Chủ kho đã chọn Người thực thi và khai xong.</summary>
    Active = 1,

    /// <summary>Người thực thi đã mở bàn giao; luật sư đang lập biên bản, cấu hình kho bị khóa.</summary>
    HandoverRequested = 4,

    /// <summary>Biên bản đã chốt; đang xác minh người nhận, ký và giao tài sản.</summary>
    HandoverInProgress = 5,

    /// <summary>Mọi lượt bàn giao đã xong.</summary>
    Closed = 6,
}
