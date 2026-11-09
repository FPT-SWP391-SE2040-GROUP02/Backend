using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Cấu hình của một dịch vụ ngoài (eKYC, SMTP, SePay, R2) do Admin sửa trên web, mỗi cặp (ServiceKey, Slot) một dòng.
/// Có dòng thì đè giá trị trong appsettings. Giá trị thường nằm trong SettingsJson; giá trị bí mật chỉ lưu dạng mã hóa envelope.
/// </summary>
public class SystemConfig
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Mã dịch vụ: EKYC, SMTP, SEPAY, R2.</summary>
    public string ServiceKey { get; set; } = string.Empty;

    public SystemConfigSlot Slot { get; set; }

    /// <summary>Bật/tắt ô dự phòng. Ô chính luôn bật.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>JSON các trường thường (model, địa chỉ, cổng...), giá trị đều là chuỗi.</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>Mã KEK đã bọc khóa dữ liệu; rỗng khi không có giá trị bí mật.</summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    /// <summary>JSON các giá trị bí mật (khóa API, mật khẩu) đã mã hóa AES-GCM bằng khóa dữ liệu.</summary>
    public byte[] SecretsEncrypted { get; set; } = Array.Empty<byte>();

    /// <summary>Tăng mỗi lần lưu; dùng chống hai Admin ghi đè nhau.</summary>
    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedByUserId { get; set; }

    public string UpdateReason { get; set; } = string.Empty;
}
