namespace LegacyVault.Application.Services;

/// <summary>
/// AI nhận diện một trang tệp Executor tải lên có phải giấy chứng tử / trích lục khai tử hay không.
/// Cài đặt ở Infrastructure (Gemini).
/// </summary>
/// <remarks>
/// Không ném lỗi khi AI không trả lời được (chưa có key, hết quota, timeout, trả lời sai định dạng):
/// trả Status = Unavailable để việc gửi hồ sơ vẫn đi tiếp và Verifier tự xem (cờ CertificateUnverified).
/// </remarks>
public interface IDeathCertificateClassifier
{
    Task<DeathCertificateCheck> ClassifyAsync(byte[] content, string contentType, CancellationToken ct);
}

public enum DeathCertificateCheckStatus
{
    /// <summary>AI nhận ra đây là giấy chứng tử / trích lục khai tử.</summary>
    Recognized = 0,
    /// <summary>AI xem được tệp và kết luận không phải giấy chứng tử.</summary>
    NotRecognized = 1,
    /// <summary>AI không kiểm tra được tệp này.</summary>
    Unavailable = 2,
}

/// <summary>Kết quả AI cho một tệp.</summary>
public sealed class DeathCertificateCheck
{
    public DeathCertificateCheckStatus Status { get; set; }

    /// <summary>Loại giấy AI nhận ra, viết ngắn bằng tiếng Việt (VD "Căn cước công dân"); null khi Unavailable.</summary>
    public string? DocumentType { get; set; }

    public static DeathCertificateCheck Unavailable()
    {
        DeathCertificateCheck check = new DeathCertificateCheck();
        check.Status = DeathCertificateCheckStatus.Unavailable;
        return check;
    }
}
