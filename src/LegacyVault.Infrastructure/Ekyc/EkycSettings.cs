namespace LegacyVault.Infrastructure.Ekyc;

/// <summary>Cấu hình mục "Ekyc" trong appsettings: địa chỉ dịch vụ eKYC (chương trình Python riêng).</summary>
public sealed class EkycSettings
{
    public const string SectionName = "Ekyc";

    /// <summary>Địa chỉ gốc của dịch vụ, ví dụ http://127.0.0.1:8879. Để trống thì mọi lệnh eKYC trả 503.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Khóa dùng chung gửi trong header X-Ekyc-Key. Dịch vụ eKYC phải kiểm tra header này trước khi mở ra ngoài localhost.
    /// Đặt bằng user-secrets hoặc biến môi trường, không commit giá trị thật.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>OCR chạy CPU và có thể thử xoay ảnh nên chậm; mặc định 60 giây.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
