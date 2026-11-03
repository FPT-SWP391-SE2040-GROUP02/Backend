namespace LegacyVault.Application.ServiceConfig;

/// <summary>Luật chung: khi nào một lần gọi dịch vụ ngoài thất bại đáng để thử sang ô dự phòng.</summary>
public static class ServiceFailover
{
    /// <summary>
    /// Lỗi do phía dịch vụ hoặc khóa/hạn mức của ô này (5xx, 401, 403, 429) thì ô khác có thể trả lời được. Mã khác
    /// (ví dụ 400, 404, 422) là lỗi của yêu cầu hoặc cấu hình, đổi ô không giúp gì nên không chuyển.
    /// </summary>
    public static bool ShouldFailOver(int httpStatusCode)
    {
        return httpStatusCode >= 500 || httpStatusCode == 401 || httpStatusCode == 403 || httpStatusCode == 429;
    }
}
