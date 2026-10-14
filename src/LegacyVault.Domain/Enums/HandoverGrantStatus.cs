namespace LegacyVault.Domain.Enums;

/// <summary>Trạng thái quyền xem/tải (grant) của người nhận (SRS §11.1).</summary>
public enum HandoverGrantStatus
{
    /// <summary>Còn hiệu lực; vẫn phải kiểm hạn ExpiresAt ở mỗi request.</summary>
    Active = 0,
    /// <summary>Bị chặn tạm (hold, nghi vấn). Chưa có chức năng đặt trạng thái này.</summary>
    Blocked = 1,
    /// <summary>Quá 30 ngày.</summary>
    Expired = 2,
    /// <summary>Bị thu hồi. Chưa có chức năng đặt trạng thái này.</summary>
    Revoked = 3,
}
