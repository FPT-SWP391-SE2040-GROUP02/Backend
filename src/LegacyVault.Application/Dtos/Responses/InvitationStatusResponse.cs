namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả kiểm link mời (công khai): chỉ hai cờ, không lộ thêm dữ liệu nào.</summary>
public class InvitationStatusResponse
{
    public bool Valid { get; set; }

    public bool ResponseExpired { get; set; }

    /// <summary>Email người nhận đã che bớt (ví dụ m•••••h@example.com); không bao giờ trả email đầy đủ. Null khi không đọc được.</summary>
    public string? InvitedEmailMasked { get; set; }

    /// <summary>
    /// Email người nhận đầy đủ (đã chuẩn hóa) cho người giữ link: để form đăng ký điền sẵn và khóa đúng email được mời.
    /// Quyết định của nhóm: ai có link thì thấy email này (thư mời đã gửi đúng tới email đó). Null khi không đọc được.
    /// </summary>
    public string? InvitedEmail { get; set; }

    /// <summary>Email được mời đã có tài khoản hay chưa; FE dùng để chọn nút "Đăng nhập" hoặc "Tạo tài khoản".</summary>
    public bool AccountExists { get; set; }

    /// <summary>
    /// Tên Chủ kho để trang cửa vào ghi "{Chủ kho} đã để lại cho bạn một gói". Thư mời gửi tới người nhận đã nêu tên này nên
    /// người giữ link không biết thêm điều gì mới. Null khi không đọc được.
    /// </summary>
    public string? OwnerFullName { get; set; }
}
