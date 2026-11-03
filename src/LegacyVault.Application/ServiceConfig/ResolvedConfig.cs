namespace LegacyVault.Application.ServiceConfig;

/// <summary>Nguồn của một cấu hình đang dùng: dòng trong DB hay appsettings của máy chủ.</summary>
public static class ConfigSources
{
    public const string Database = "DATABASE";
    public const string AppSettings = "APPSETTINGS";
}

/// <summary>
/// Cấu hình đang có hiệu lực của một ô. Settings luôn đủ các trường thường của danh mục (thiếu thì điền mặc định);
/// Secrets chỉ có tên nào đã có giá trị. Đối tượng được cache nên người dùng không được sửa nội dung.
/// </summary>
public sealed class ResolvedConfig
{
    public string Source { get; set; } = string.Empty;

    public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

    public Dictionary<string, string> Secrets { get; set; } = new Dictionary<string, string>();
}
