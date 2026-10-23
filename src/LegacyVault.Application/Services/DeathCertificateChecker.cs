using System.Security.Cryptography;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>Kết quả AI cho một tệp, trả cho Executor khi bị chặn gửi (không có nội dung tệp hay dữ liệu cá nhân).</summary>
public sealed class DeathCertificateFileCheck
{
    public Guid FileId { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>RECOGNIZED | NOT_RECOGNIZED | UNAVAILABLE.</summary>
    public string Status { get; set; } = string.Empty;

    public string? DocumentType { get; set; }
}

/// <summary>Kết quả kiểm tra cả bản hồ sơ.</summary>
public sealed class DeathCertificateCheckResult
{
    public DeathCertificateCheckStatus Status { get; set; }

    public List<DeathCertificateFileCheck> Files { get; set; } = new List<DeathCertificateFileCheck>();
}

/// <summary>
/// Đọc lần lượt các trang giấy của một bản hồ sơ và hỏi AI có phải giấy chứng tử không.
/// Dừng ngay ở tệp đầu tiên AI nhận ra để đỡ tốn lượt gọi.
/// </summary>
public sealed class DeathCertificateChecker
{
    private readonly IAssetContentStore contentStore;
    private readonly IDeathCertificateClassifier classifier;

    public DeathCertificateChecker(IAssetContentStore contentStore, IDeathCertificateClassifier classifier)
    {
        this.contentStore = contentStore;
        this.classifier = classifier;
    }

    public async Task<DeathCertificateCheckResult> CheckAsync(DeathCaseVersion version, CancellationToken ct)
    {
        List<DeathCaseFile> files = version.Files.OrderBy(f => f.Position).ToList();
        List<DeathCertificateCheckStatus> statuses = new List<DeathCertificateCheckStatus>();
        DeathCertificateCheckResult result = new DeathCertificateCheckResult();

        foreach (DeathCaseFile file in files)
        {
            DeathCertificateCheck check = await CheckFileAsync(file, ct);
            statuses.Add(check.Status);

            DeathCertificateFileCheck fileCheck = new DeathCertificateFileCheck();
            fileCheck.FileId = file.Id;
            fileCheck.FileName = file.FileName;
            fileCheck.Status = check.Status.ToApiString();
            fileCheck.DocumentType = check.DocumentType;
            result.Files.Add(fileCheck);

            if (check.Status == DeathCertificateCheckStatus.Recognized)
            {
                break;
            }
        }

        result.Status = DeathCertificateCheckRules.Combine(statuses);
        return result;
    }

    private async Task<DeathCertificateCheck> CheckFileAsync(DeathCaseFile file, CancellationToken ct)
    {
        if (file.SizeBytes > DeathCertificateCheckRules.MaxCheckBytes)
        {
            return DeathCertificateCheck.Unavailable();
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = file.StorageKey;
        stored.SizeBytes = file.SizeBytes;
        stored.Sha256 = file.Sha256;
        stored.EncryptionKeyId = file.EncryptionKeyId;
        stored.WrappedDataKey = file.WrappedDataKey;

        byte[] content;
        try
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                await contentStore.CopyToAsync(stored, buffer, ct);
                content = buffer.ToArray();
            }
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            // Tệp hỏng không phải việc của bước AI: để Verifier thấy khi mở tệp, ở đây chỉ coi như chưa kiểm tra được.
            return DeathCertificateCheck.Unavailable();
        }

        return await classifier.ClassifyAsync(content, file.ContentType, ct);
    }
}
