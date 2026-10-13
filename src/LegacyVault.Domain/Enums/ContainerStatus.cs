namespace LegacyVault.Domain.Enums;

/// <summary>
/// Trạng thái Container (gói bàn giao), SETUP-16.
/// </summary>
/// <remarks>
/// Draft/Ready do backend TỰ TÍNH lại sau mỗi thay đổi; FE không gửi trạng thái đích (STATE-01).
/// Chỉ Archived là do Owner chủ động chọn.
/// </remarks>
public enum ContainerStatus
{
    /// <summary>Thiếu tài sản hoặc thiếu chỉ định hợp lệ. Nằm ngoài kế hoạch (SETUP-01).</summary>
    Draft = 0,
    /// <summary>Có ít nhất một tài sản VÀ một chỉ định hợp lệ.</summary>
    Ready = 1,
    /// <summary>Owner lưu trữ: không thuộc kế hoạch, không chứa tài sản, chỉ định đã gỡ.</summary>
    Archived = 2,
}
