namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một địa chỉ email mà máy chủ thư báo không giao được (bounce). Chỉ lưu băm của email, không lưu email thô.
/// </summary>
public class EmailBounce
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 (hex thường) của email đã chuẩn hóa chữ thường.</summary>
    public string EmailHash { get; set; } = string.Empty;

    /// <summary>Mã lỗi SMTP của thư báo lỗi, ví dụ "5.1.1" (không có hộp thư) hoặc "5.4.4" (không tìm thấy tên miền).</summary>
    public string FailureCode { get; set; } = string.Empty;

    public DateTimeOffset DetectedAt { get; set; }
}
