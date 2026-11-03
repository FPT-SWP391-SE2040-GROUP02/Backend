namespace LegacyVault.Application.ServiceConfig;

public enum ConfigFieldKind
{
    /// <summary>Chuỗi; nếu có Pattern thì phải khớp Pattern, ngược lại tối đa Max ký tự.</summary>
    Text,
    /// <summary>Số nguyên trong khoảng [Min, Max].</summary>
    Integer,
    /// <summary>Địa chỉ http hoặc https, không kèm tài khoản, tối đa 2048 ký tự.</summary>
    Url,
    /// <summary>Một trong các giá trị của Choices, khớp chính xác từng ký tự.</summary>
    Choice,
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

    /// <summary>Các giá trị hợp lệ của trường loại Choice.</summary>
    public List<string> Choices { get; set; } = new List<string>();

    /// <summary>Giá trị dùng khi appsettings không đặt trường này (chuỗi, kể cả số).</summary>
    public string DefaultValue { get; set; } = string.Empty;
}

/// <summary>Danh mục các dịch vụ đã nối vào trang cấu hình và trường của từng dịch vụ.</summary>
public static class SystemConfigCatalog
{
    /// <summary>Tên model nằm trong URL gọi Gemini nên chỉ cho chữ, số, dấu chấm, gạch ngang và gạch dưới.</summary>
    private const string GeminiModelPattern = "^[A-Za-z0-9._-]{1,100}$";

    private static readonly IReadOnlyList<ConfigFieldSpec> GeminiFields = BuildGeminiFields();

    private static readonly IReadOnlyList<ConfigFieldSpec> EkycFields = BuildEkycFields();

    private static readonly IReadOnlyList<ConfigFieldSpec> SePayFields = BuildSePayFields();

    private static readonly IReadOnlyList<ConfigFieldSpec> SmtpFields = BuildSmtpFields();

    private static readonly IReadOnlyList<ConfigFieldSpec> R2Fields = BuildR2Fields();

    public static bool IsWired(string serviceKey)
    {
        return WiredServices().Contains(serviceKey);
    }

    public static IReadOnlyList<string> WiredServices()
    {
        return new List<string>
        {
            SystemConfigServices.Gemini, SystemConfigServices.Ekyc, SystemConfigServices.Smtp, SystemConfigServices.SePay,
            SystemConfigServices.R2,
        };
    }

    /// <summary>SePay và R2 không tự chuyển dịch vụ: đổi nơi nhận tiền hay bucket tự động có thể làm sai đối soát hoặc mất truy cập dữ liệu.</summary>
    public static bool SupportsFallback(string serviceKey)
    {
        return serviceKey == SystemConfigServices.Gemini
            || serviceKey == SystemConfigServices.Ekyc
            || serviceKey == SystemConfigServices.Smtp;
    }

    /// <summary>Các trường của dịch vụ; dịch vụ chưa nối thì danh sách rỗng.</summary>
    public static IReadOnlyList<ConfigFieldSpec> Fields(string serviceKey)
    {
        if (serviceKey == SystemConfigServices.Gemini)
        {
            return GeminiFields;
        }
        if (serviceKey == SystemConfigServices.Ekyc)
        {
            return EkycFields;
        }
        if (serviceKey == SystemConfigServices.Smtp)
        {
            return SmtpFields;
        }
        if (serviceKey == SystemConfigServices.SePay)
        {
            return SePayFields;
        }
        if (serviceKey == SystemConfigServices.R2)
        {
            return R2Fields;
        }
        return new List<ConfigFieldSpec>();
    }

    /// <summary>
    /// Thay đổi này có cần Admin gõ lại tên dịch vụ để xác nhận không. R2: đổi tài khoản hoặc bucket làm kho mất truy cập
    /// dữ liệu cũ. SePay: chuyển sang Live là bắt đầu nhận tiền thật. before/after là các trường thường đang có và sắp lưu.
    /// </summary>
    public static bool RequiresConfirm(
        string serviceKey, IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        if (serviceKey == SystemConfigServices.SePay)
        {
            return ValueOf(after, "environment") == "Live" && ValueOf(before, "environment") != "Live";
        }
        if (serviceKey == SystemConfigServices.R2)
        {
            return ValueOf(after, "accountId") != ValueOf(before, "accountId")
                || ValueOf(after, "bucketName") != ValueOf(before, "bucketName");
        }
        return false;
    }

    private static string ValueOf(IReadOnlyDictionary<string, string> values, string name)
    {
        string? value = null;
        if (values.TryGetValue(name, out value))
        {
            return value;
        }
        return string.Empty;
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

    /// <summary>
    /// R2: tài khoản và bucket Cloudflare lưu nội dung tài sản đã mã hóa, cùng cặp khóa truy cập. Tài khoản nằm trong tên máy
    /// của địa chỉ gọi nên chỉ nhận chữ, số và gạch ngang; tên bucket theo luật đặt tên của R2. Không có ô dự phòng vì chuyển
    /// sang bucket khác làm kho mất truy cập vào dữ liệu đã lưu ở bucket cũ.
    /// </summary>
    private static IReadOnlyList<ConfigFieldSpec> BuildR2Fields()
    {
        ConfigFieldSpec accountId = new ConfigFieldSpec();
        accountId.Name = "accountId";
        accountId.Kind = ConfigFieldKind.Text;
        accountId.Required = true;
        accountId.Pattern = "^[A-Za-z0-9-]{1,64}$";

        ConfigFieldSpec bucketName = new ConfigFieldSpec();
        bucketName.Name = "bucketName";
        bucketName.Kind = ConfigFieldKind.Text;
        bucketName.Required = true;
        bucketName.Pattern = "^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$";

        ConfigFieldSpec accessKeyId = new ConfigFieldSpec();
        accessKeyId.Name = "accessKeyId";
        accessKeyId.Kind = ConfigFieldKind.Text;
        accessKeyId.Secret = true;
        accessKeyId.Required = true;
        accessKeyId.Max = 256;

        ConfigFieldSpec secretAccessKey = new ConfigFieldSpec();
        secretAccessKey.Name = "secretAccessKey";
        secretAccessKey.Kind = ConfigFieldKind.Text;
        secretAccessKey.Secret = true;
        secretAccessKey.Required = true;
        secretAccessKey.Max = 512;

        return new List<ConfigFieldSpec> { accountId, bucketName, accessKeyId, secretAccessKey };
    }

    /// <summary>
    /// Email: máy chủ SMTP (mặc định Gmail) hoặc Webhook HTTPS (Google Apps Script, qua cổng 443 khi nhà cung cấp chặn cổng SMTP).
    /// Có địa chỉ Webhook thì gửi qua Webhook, không thì SMTP. Ô dự phòng thường chỉ đặt Webhook.
    /// </summary>
    private static IReadOnlyList<ConfigFieldSpec> BuildSmtpFields()
    {
        ConfigFieldSpec host = new ConfigFieldSpec();
        host.Name = "host";
        host.Kind = ConfigFieldKind.Text;
        host.Required = true;
        host.Max = 255;
        host.DefaultValue = "smtp.gmail.com";

        ConfigFieldSpec port = new ConfigFieldSpec();
        port.Name = "port";
        port.Kind = ConfigFieldKind.Integer;
        port.Required = true;
        port.Min = 1;
        port.Max = 65535;
        port.DefaultValue = "587";

        ConfigFieldSpec secureSocket = new ConfigFieldSpec();
        secureSocket.Name = "secureSocket";
        secureSocket.Kind = ConfigFieldKind.Choice;
        secureSocket.Required = true;
        secureSocket.Choices = new List<string> { "None", "Auto", "SslOnConnect", "StartTls", "StartTlsWhenAvailable" };
        secureSocket.DefaultValue = "StartTls";

        ConfigFieldSpec userName = new ConfigFieldSpec();
        userName.Name = "userName";
        userName.Kind = ConfigFieldKind.Text;
        userName.Max = 256;

        ConfigFieldSpec fromAddress = new ConfigFieldSpec();
        fromAddress.Name = "fromAddress";
        fromAddress.Kind = ConfigFieldKind.Text;
        fromAddress.Max = 256;

        ConfigFieldSpec fromName = new ConfigFieldSpec();
        fromName.Name = "fromName";
        fromName.Kind = ConfigFieldKind.Text;
        fromName.Max = 100;
        fromName.DefaultValue = "LegacyVault";

        ConfigFieldSpec timeout = new ConfigFieldSpec();
        timeout.Name = "timeoutSeconds";
        timeout.Kind = ConfigFieldKind.Integer;
        timeout.Required = true;
        timeout.Min = 5;
        timeout.Max = 120;
        timeout.DefaultValue = "20";

        ConfigFieldSpec webhookUrl = new ConfigFieldSpec();
        webhookUrl.Name = "webhookUrl";
        webhookUrl.Kind = ConfigFieldKind.Url;

        ConfigFieldSpec password = new ConfigFieldSpec();
        password.Name = "password";
        password.Kind = ConfigFieldKind.Text;
        password.Secret = true;
        password.Max = 512;

        return new List<ConfigFieldSpec>
        {
            host, port, secureSocket, userName, fromAddress, fromName, timeout, webhookUrl, password,
        };
    }

    /// <summary>
    /// SePay: môi trường (Test hoặc Live, PAY-01), tài khoản nhận tiền hiện QR cho Owner (thông tin công khai) và khóa
    /// SePay gửi trong header Authorization khi gọi webhook. Chưa đặt tài khoản thì không tạo được đơn thanh toán.
    /// </summary>
    private static IReadOnlyList<ConfigFieldSpec> BuildSePayFields()
    {
        ConfigFieldSpec environment = new ConfigFieldSpec();
        environment.Name = "environment";
        environment.Kind = ConfigFieldKind.Choice;
        environment.Required = true;
        environment.Choices = new List<string> { "Test", "Live" };
        environment.DefaultValue = "Test";

        ConfigFieldSpec bankCode = new ConfigFieldSpec();
        bankCode.Name = "bankCode";
        bankCode.Kind = ConfigFieldKind.Text;
        bankCode.Max = 32;

        ConfigFieldSpec accountNumber = new ConfigFieldSpec();
        accountNumber.Name = "accountNumber";
        accountNumber.Kind = ConfigFieldKind.Text;
        accountNumber.Max = 32;

        ConfigFieldSpec accountName = new ConfigFieldSpec();
        accountName.Name = "accountName";
        accountName.Kind = ConfigFieldKind.Text;
        accountName.Max = 100;

        ConfigFieldSpec webhookApiKey = new ConfigFieldSpec();
        webhookApiKey.Name = "webhookApiKey";
        webhookApiKey.Kind = ConfigFieldKind.Text;
        webhookApiKey.Secret = true;
        webhookApiKey.Max = 512;

        return new List<ConfigFieldSpec> { environment, bankCode, accountNumber, accountName, webhookApiKey };
    }

    /// <summary>eKYC là chương trình Python riêng: địa chỉ, khóa dùng chung (header X-Ekyc-Key, tùy chọn) và thời gian chờ (OCR chạy CPU nên lâu).</summary>
    private static IReadOnlyList<ConfigFieldSpec> BuildEkycFields()
    {
        ConfigFieldSpec baseUrl = new ConfigFieldSpec();
        baseUrl.Name = "baseUrl";
        baseUrl.Kind = ConfigFieldKind.Url;
        baseUrl.Required = true;

        ConfigFieldSpec timeout = new ConfigFieldSpec();
        timeout.Name = "timeoutSeconds";
        timeout.Kind = ConfigFieldKind.Integer;
        timeout.Required = true;
        timeout.Min = 5;
        timeout.Max = 300;
        timeout.DefaultValue = "60";

        ConfigFieldSpec apiKey = new ConfigFieldSpec();
        apiKey.Name = "apiKey";
        apiKey.Kind = ConfigFieldKind.Text;
        apiKey.Secret = true;
        apiKey.Max = 512;

        return new List<ConfigFieldSpec> { baseUrl, timeout, apiKey };
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
