namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>Địa chỉ và header gọi Gemini, dùng chung cho nhận diện giấy chứng tử và nút thử kết nối.</summary>
public static class GeminiEndpoint
{
    public const string ApiKeyHeader = "X-goog-api-key";

    /// <summary>Tên model nằm trong đường dẫn nên luôn được mã hóa; SystemConfigValidator đã giới hạn ký tự từ trước.</summary>
    public static string GenerateContentUrl(string model)
    {
        return $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
    }
}
