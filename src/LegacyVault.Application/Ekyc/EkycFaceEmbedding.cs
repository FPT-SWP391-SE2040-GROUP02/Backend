namespace LegacyVault.Application.Ekyc;

/// <summary>Vector đặc trưng khuôn mặt do dịch vụ eKYC trích từ một ảnh. Dữ liệu sinh trắc: không log, không trả cho FE.</summary>
public sealed class EkycFaceEmbedding
{
    /// <summary>Tên model trích đặc trưng (ví dụ "sface"). Chỉ vector cùng model mới so được với nhau.</summary>
    public string ModelName { get; set; } = string.Empty;

    public float[] Values { get; set; } = Array.Empty<float>();
}
