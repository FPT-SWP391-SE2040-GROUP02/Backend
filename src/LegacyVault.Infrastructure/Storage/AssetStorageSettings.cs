namespace LegacyVault.Infrastructure.Storage;

/// <summary>Cấu hình mục "AssetStorage" trong appsettings.</summary>
public sealed class AssetStorageSettings
{
    /// <summary>Nơi lưu nội dung đã mã hóa: "Local" (đĩa server, mặc định) hoặc "R2" (Cloudflare R2).</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>Thư mục lưu nội dung đã mã hóa; đường dẫn tương đối tính từ ContentRoot của Api. Không được nằm trong wwwroot.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Cấu hình bucket R2; chỉ dùng khi Provider là "R2".</summary>
    public R2StorageSettings R2 { get; set; } = new R2StorageSettings();

    public bool IsR2()
    {
        return string.Equals(Provider, "R2", StringComparison.OrdinalIgnoreCase);
    }
}
