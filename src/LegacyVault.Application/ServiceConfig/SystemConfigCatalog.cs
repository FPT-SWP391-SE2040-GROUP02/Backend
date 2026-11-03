namespace LegacyVault.Application.ServiceConfig;

public enum ConfigFieldKind
{
    /// <summary>Chuỗi; nếu có Pattern thì phải khớp Pattern, ngược lại tối đa Max ký tự.</summary>
    Text,
    /// <summary>Số nguyên trong khoảng [Min, Max].</summary>
    Integer,
}

/// <summary>Mô tả một trường cấu hình của dịch vụ ngoài: tên, loại, có bí mật hay không và luật kiểm tra.</summary>
public sealed class ConfigFieldSpec
{
    public string Name { get; set; } = string.Empty;

    /// <summary>true thì giá trị chỉ lưu mã hóa và không bao giờ trả về qua API.</summary>
    public bool Secret { get; set; }

    public bool Required { get; set; }

    public ConfigFieldKind Kind { get; set; }

    public int Min { get; set; }

    public int Max { get; set; }

    public string? Pattern { get; set; }

    /// <summary>Giá trị dùng khi appsettings không đặt trường này (chuỗi, kể cả số).</summary>
    public string DefaultValue { get; set; } = string.Empty;
}

/// <summary>Danh mục các dịch vụ đã nối vào trang cấu hình và trường của từng dịch vụ.</summary>
public static class SystemConfigCatalog
{
    /// <summary>Tên model nằm trong URL gọi Gemini nên chỉ cho chữ, số, dấu chấm, gạch ngang và gạch dưới.</summary>
    private const string GeminiModelPattern = "^[A-Za-z0-9._-]{1,100}$";

    private static readonly IReadOnlyList<ConfigFieldSpec> GeminiFields = BuildGeminiFields();

    public static bool IsWired(string serviceKey)
    {
        return serviceKey == SystemConfigServices.Gemini;
    }

    public static IReadOnlyList<string> WiredServices()
    {
        return new List<string> { SystemConfigServices.Gemini };
    }

    public static bool SupportsFallback(string serviceKey)
    {
        return serviceKey == SystemConfigServices.Gemini;
    }

    /// <summary>Các trường của dịch vụ; dịch vụ chưa nối thì danh sách rỗng.</summary>
    public static IReadOnlyList<ConfigFieldSpec> Fields(string serviceKey)
    {
        if (serviceKey == SystemConfigServices.Gemini)
        {
            return GeminiFields;
        }
        return new List<ConfigFieldSpec>();
    }

    /// <summary>Giá trị mặc định của các trường thường (không bí mật) của dịch vụ.</summary>
    public static Dictionary<string, string> DefaultSettings(string serviceKey)
    {
        Dictionary<string, string> defaults = new Dictionary<string, string>();
        foreach (ConfigFieldSpec field in Fields(serviceKey))
        {
            if (!field.Secret)
            {
                defaults[field.Name] = field.DefaultValue;
            }
        }
        return defaults;
    }

    private static IReadOnlyList<ConfigFieldSpec> BuildGeminiFields()
    {
        ConfigFieldSpec model = new ConfigFieldSpec();
        model.Name = "model";
        model.Kind = ConfigFieldKind.Text;
        model.Required = true;
        model.Pattern = GeminiModelPattern;
        model.DefaultValue = "gemini-flash-lite-latest";

        ConfigFieldSpec timeout = new ConfigFieldSpec();
        timeout.Name = "timeoutSeconds";
        timeout.Kind = ConfigFieldKind.Integer;
        timeout.Required = true;
        timeout.Min = 5;
        timeout.Max = 60;
        timeout.DefaultValue = "20";

        ConfigFieldSpec apiKey = new ConfigFieldSpec();
        apiKey.Name = "apiKey";
        apiKey.Kind = ConfigFieldKind.Text;
        apiKey.Secret = true;
        apiKey.Required = true;
        apiKey.Max = 512;

        return new List<ConfigFieldSpec> { model, timeout, apiKey };
    }
}
