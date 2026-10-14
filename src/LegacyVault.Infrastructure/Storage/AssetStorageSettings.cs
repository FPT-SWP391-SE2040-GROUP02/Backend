namespace LegacyVault.Infrastructure.Storage;

/// <summary>Cấu hình mục "AssetStorage" trong appsettings.</summary>
public sealed class AssetStorageSettings
{
    /// <summary>Thư mục lưu nội dung đã mã hóa; đường dẫn tương đối tính từ ContentRoot của Api. Không được nằm trong wwwroot.</summary>
    public string RootPath { get; set; } = string.Empty;
}
