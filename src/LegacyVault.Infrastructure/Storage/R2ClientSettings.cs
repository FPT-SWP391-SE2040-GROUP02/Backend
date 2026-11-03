using System.Security.Cryptography;
using System.Text;
using Amazon.S3;
using LegacyVault.Application.ServiceConfig;

namespace LegacyVault.Infrastructure.Storage;

/// <summary>Phần dùng chung giữa client R2 và bộ thử kết nối: đổi cấu hình thành R2StorageSettings, dựng cấu hình S3, dấu vân tay bộ giá trị.</summary>
internal static class R2ClientSettings
{
    /// <summary>Thiếu giá trị nào thì để rỗng (FindError của R2StorageSettings báo thiếu).</summary>
    public static R2StorageSettings FromConfig(ResolvedConfig? config)
    {
        R2StorageSettings settings = new R2StorageSettings();
        if (config == null)
        {
            return settings;
        }
        settings.AccountId = Value(config.Settings, "accountId");
        settings.BucketName = Value(config.Settings, "bucketName");
        settings.AccessKeyId = Value(config.Secrets, "accessKeyId");
        settings.SecretAccessKey = Value(config.Secrets, "secretAccessKey");
        return settings;
    }

    /// <summary>R2 dùng vùng "auto", path-style và checksum WHEN_REQUIRED (R2 không hỗ trợ checksum tự động mặc định của SDK mới).</summary>
    public static AmazonS3Config BuildConfig(R2StorageSettings settings)
    {
        AmazonS3Config config = new AmazonS3Config();
        config.ServiceURL = $"https://{settings.AccountId}.r2.cloudflarestorage.com";
        config.AuthenticationRegion = "auto";
        config.ForcePathStyle = true;
        config.RequestChecksumCalculation = Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED;
        config.ResponseChecksumValidation = Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED;
        return config;
    }

    /// <summary>Dấu vân tay của bộ giá trị dùng để biết khi nào phải tạo lại client; không chứa khóa ở dạng đọc được.</summary>
    public static string Fingerprint(R2StorageSettings settings)
    {
        string raw = settings.AccountId + "|" + settings.AccessKeyId + "|" + settings.SecretAccessKey;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static string Value(Dictionary<string, string> values, string name)
    {
        string? value = null;
        if (values.TryGetValue(name, out value))
        {
            return value;
        }
        return string.Empty;
    }
}
