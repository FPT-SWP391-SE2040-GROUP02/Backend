namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Cấu hình mục "AssetStorage:R2": bucket Cloudflare R2 private lưu nội dung tài sản đã mã hóa.
/// </summary>
/// <remarks>
/// Khóa truy cập KHÔNG nằm trong appsettings; đặt bằng <c>dotnet user-secrets</c> hoặc biến môi trường
/// (ví dụ <c>AssetStorage__R2__SecretAccessKey</c>). Không log các giá trị này.
/// </remarks>
public sealed class R2StorageSettings
{
    /// <summary>Account ID của Cloudflare; dùng dựng địa chỉ https://{AccountId}.r2.cloudflarestorage.com.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Tên bucket (private, không bật truy cập công khai).</summary>
    public string BucketName { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Kiểm tra đủ bốn giá trị; trả thông báo lỗi hoặc null nếu hợp lệ.</summary>
    public string? FindError()
    {
        if (string.IsNullOrWhiteSpace(AccountId)
            || string.IsNullOrWhiteSpace(BucketName)
            || string.IsNullOrWhiteSpace(AccessKeyId)
            || string.IsNullOrWhiteSpace(SecretAccessKey))
        {
            return "Thiếu AssetStorage:R2 (cần AccountId, BucketName, AccessKeyId, SecretAccessKey).";
        }
        return null;
    }
}
