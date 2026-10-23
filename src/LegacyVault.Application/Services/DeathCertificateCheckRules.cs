namespace LegacyVault.Application.Services;

/// <summary>
/// Luật dùng kết quả AI nhận diện giấy chứng tử khi Executor gửi hồ sơ. Hàm thuần, test không cần DB.
/// </summary>
public static class DeathCertificateCheckRules
{
    /// <summary>Mã trong missingRequirements khi AI xem hết các tệp mà không tệp nào là giấy chứng tử.</summary>
    public const string NotRecognizedCode = "DEATH_CERTIFICATE_NOT_RECOGNIZED";

    /// <summary>AI phải chắc ít nhất mức này mới tính là đã nhận ra giấy chứng tử.</summary>
    public const double MinConfidence = 0.7;

    /// <summary>
    /// Tệp lớn hơn mức này không gửi cho AI (giới hạn gửi kèm trực tiếp của Gemini khoảng 20 MB sau khi mã hóa base64).
    /// </summary>
    public const long MaxCheckBytes = 14L * 1024 * 1024;

    /// <summary>Đổi câu trả lời của AI thành trạng thái: chỉ tính là giấy chứng tử khi AI nói có và đủ chắc.</summary>
    public static DeathCertificateCheckStatus ToStatus(bool isDeathCertificate, double confidence)
    {
        if (isDeathCertificate && confidence >= MinConfidence)
        {
            return DeathCertificateCheckStatus.Recognized;
        }
        return DeathCertificateCheckStatus.NotRecognized;
    }

    /// <summary>
    /// Gộp kết quả các tệp: có một tệp nhận ra → Recognized; không có nhưng có tệp AI không kiểm tra được → Unavailable
    /// (vẫn cho gửi, đặt cờ); mọi tệp đều không phải giấy chứng tử → NotRecognized (chặn gửi).
    /// Danh sách rỗng → Unavailable (không có gì để kết luận; luật đủ tệp đã chặn trường hợp này trước đó).
    /// </summary>
    public static DeathCertificateCheckStatus Combine(List<DeathCertificateCheckStatus> statuses)
    {
        bool anyUnavailable = false;
        foreach (DeathCertificateCheckStatus status in statuses)
        {
            if (status == DeathCertificateCheckStatus.Recognized)
            {
                return DeathCertificateCheckStatus.Recognized;
            }
            if (status == DeathCertificateCheckStatus.Unavailable)
            {
                anyUnavailable = true;
            }
        }
        if (anyUnavailable || statuses.Count == 0)
        {
            return DeathCertificateCheckStatus.Unavailable;
        }
        return DeathCertificateCheckStatus.NotRecognized;
    }
}
