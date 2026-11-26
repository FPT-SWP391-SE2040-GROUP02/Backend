namespace LegacyVault.Domain.Enums;

/// <summary>
/// Biên bản bàn giao đang ở bước nào: lập biên bản → đã chốt, đang giao (mỗi người nhận đi riêng: danh tính → ký →
/// được mở quyền tải) → hoàn tất khi mọi người nhận đã ký.
/// Giá trị lưu trong DB là tên enum; không đổi tên giá trị đã có.
/// </summary>
public enum HandoverRecordStatus
{
    /// <summary>Bước 1: luật sư đang lập hoặc sửa danh sách người nhận và tài sản.</summary>
    Drafting = 0,

    /// <summary>
    /// Không còn dùng cho biên bản mới (chỉ có ở biên bản chốt trước khi giao riêng từng người); xử lý như Signing.
    /// </summary>
    IdentityCheck = 1,

    /// <summary>
    /// Đã chốt, đang giao: luật sư và Người thực thi ký; mỗi người nhận ký khi danh tính của mình xong, rồi được mở
    /// quyền tải ngay khi đủ chữ ký luật sư, Người thực thi và của chính mình.
    /// </summary>
    Signing = 2,

    /// <summary>Mọi người nhận trong biên bản đã ký (cùng luật sư và Người thực thi): đã giao xong cho mọi người.</summary>
    Completed = 3,

    /// <summary>Chủ tài sản hủy trước khi đủ chữ ký, hoặc luật sư hủy khi đang lập; kho mở khóa, kế hoạch về Đang hoạt động.</summary>
    Cancelled = 4,
}
