namespace LegacyVault.Domain.Enums;

/// <summary>
/// Biên bản bàn giao đang ở bước nào. Luật sư đi đúng thứ tự: xét giấy tờ và lập biên bản → danh tính người nhận →
/// chữ ký ba bên.
/// Giá trị lưu trong DB là tên enum; không đổi tên giá trị đã có.
/// </summary>
public enum HandoverRecordStatus
{
    /// <summary>Bước 1: luật sư đang lập hoặc sửa danh sách người nhận và tài sản.</summary>
    Drafting = 0,

    /// <summary>Bước 2: biên bản đã chốt, chờ luật sư xác nhận danh tính từng người nhận.</summary>
    IdentityCheck = 1,

    /// <summary>Bước 3: mọi người nhận đã được xác nhận danh tính, chờ đủ chữ ký ba bên.</summary>
    Signing = 2,

    /// <summary>Đủ chữ ký: người nhận được mở quyền tải, Người thực thi gửi mã nhận.</summary>
    Completed = 3,

    /// <summary>Chủ kho hủy yêu cầu nhận tài sản trước khi đủ chữ ký; kho mở khóa, kế hoạch về Đang hoạt động.</summary>
    Cancelled = 4,
}
