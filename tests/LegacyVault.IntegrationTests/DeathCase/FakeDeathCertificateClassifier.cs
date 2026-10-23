using LegacyVault.Application.Services;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// AI nhận diện giấy chứng tử giả: không gọi Gemini thật. Mặc định nhận ra mọi tệp để các test gửi hồ sơ cũ chạy như trước.
/// </summary>
public sealed class FakeDeathCertificateClassifier : IDeathCertificateClassifier
{
    public DeathCertificateCheckStatus Status { get; set; } = DeathCertificateCheckStatus.Recognized;

    public string? DocumentType { get; set; } = "Trích lục khai tử";

    public int CallCount { get; private set; }

    public Task<DeathCertificateCheck> ClassifyAsync(byte[] content, string contentType, CancellationToken ct)
    {
        CallCount = CallCount + 1;
        DeathCertificateCheck check = new DeathCertificateCheck();
        check.Status = Status;
        check.DocumentType = DocumentType;
        return Task.FromResult(check);
    }
}
