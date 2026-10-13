namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Cấu hình mục "AssetEncryption" trong appsettings: các KEK (khóa bọc DEK) dùng cho mã hóa nội dung tài sản.
/// </summary>
/// <remarks>
/// - KEK KHÔNG nằm trong DB. Server thật đặt KEK qua biến môi trường, ví dụ
///   <c>AssetEncryption__ActiveKeyId=prod-2026-10</c> và <c>AssetEncryption__Keys__prod-2026-10=&lt;base64 32 byte&gt;</c>.
///   Tạo khóa mới (PowerShell):
///   <c>$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)</c>
/// - Đổi KEK (rotation): thêm khóa mới vào Keys, chuyển ActiveKeyId sang khóa mới, GIỮ khóa cũ để còn mở được
///   các phiên bản đã lưu trước đó (mỗi phiên bản ghi lại EncryptionKeyId của KEK đã bọc nó).
/// - Không log giá trị khóa.
/// </remarks>
public sealed class AssetEncryptionSettings
{
    /// <summary>Id của KEK dùng để bọc DEK cho nội dung MỚI. Phải có trong Keys.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    /// <summary>Id KEK → giá trị KEK dạng base64 (đúng 32 byte sau khi giải mã).</summary>
    public Dictionary<string, string> Keys { get; set; } = new Dictionary<string, string>();
}
