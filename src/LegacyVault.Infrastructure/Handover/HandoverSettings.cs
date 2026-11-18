namespace LegacyVault.Infrastructure.Handover;

/// <summary>Cấu hình mục "Handover".</summary>
public sealed class HandoverSettings
{
    public const string SectionName = "Handover";

    /// <summary>
    /// Mặc định true. Đặt false (bỏ qua xác minh danh tính khi eKYC chưa có) chỉ hợp lệ ở môi trường Development;
    /// môi trường khác mà false thì API không khởi động được (xem HandoverSettingsValidator).
    /// </summary>
    public bool RequireIdentityVerification { get; set; } = true;
}
