namespace LegacyVault.Domain.Enums;

/// <summary>Ô cấu hình của một dịch vụ ngoài: chính hoặc dự phòng (chỉ có ở dịch vụ hỗ trợ dự phòng).</summary>
public enum SystemConfigSlot
{
    Primary = 0,
    Fallback = 1,
}
