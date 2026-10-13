namespace LegacyVault.Domain.Enums;

/// <summary>Trạng thái kho (SRS §11.1). Độc lập với trạng thái kế hoạch và quyền dịch vụ.</summary>
public enum VaultStatus
{
    /// <summary>Kho đang dùng. Kho mới luôn Open.</summary>
    Open = 0,
    /// <summary>Chỉ khi kế hoạch Closed và mọi payload đã xóa (SETUP-18). Định danh/lịch sử vẫn giữ.</summary>
    Closed = 1,
}
