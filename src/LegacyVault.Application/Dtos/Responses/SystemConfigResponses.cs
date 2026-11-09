namespace LegacyVault.Application.Dtos.Responses;

public class SystemConfigListResponse
{
    public List<SystemConfigServiceResponse> Services { get; set; } = new List<SystemConfigServiceResponse>();
}

public class SystemConfigServiceResponse
{
    /// <summary>EKYC, SMTP, SEPAY hoặc R2 (chỉ những dịch vụ đã nối).</summary>
    public string ServiceKey { get; set; } = string.Empty;

    public bool SupportsFallback { get; set; }

    /// <summary>Ô PRIMARY luôn có; ô FALLBACK chỉ có khi dịch vụ hỗ trợ dự phòng.</summary>
    public List<SystemConfigSlotResponse> Slots { get; set; } = new List<SystemConfigSlotResponse>();

    public ServiceStatusResponse Status { get; set; } = new ServiceStatusResponse();
}

public class SystemConfigSlotResponse
{
    /// <summary>PRIMARY | FALLBACK.</summary>
    public string Slot { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    /// <summary>DATABASE (Admin đã lưu trên web) | APPSETTINGS (đang dùng cấu hình máy chủ).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Các trường thường, giá trị chuỗi.</summary>
    public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

    /// <summary>Chỉ cho biết đã có giá trị bí mật hay chưa; không bao giờ có giá trị.</summary>
    public List<SystemConfigSecretResponse> Secrets { get; set; } = new List<SystemConfigSecretResponse>();

    /// <summary>0 khi ô chưa có dòng trong DB; gửi lại đúng giá trị này khi lưu.</summary>
    public int Version { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Họ tên (hoặc email) Admin lưu lần cuối; null khi ô chưa có dòng.</summary>
    public string? UpdatedBy { get; set; }
}

public class SystemConfigSecretResponse
{
    public string Name { get; set; } = string.Empty;

    public bool HasValue { get; set; }

    /// <summary>4 ký tự cuối của khóa đã lưu trong DB (khi khóa dài từ 8 ký tự); null nếu không hiện được.</summary>
    public string? Last4 { get; set; }
}

public class TestSystemConfigResponse
{
    public bool Ok { get; set; }

    /// <summary>Mã lỗi ngắn khi Ok = false (HTTP_401, TIMEOUT, NETWORK...); null khi thành công.</summary>
    public string? ErrorCode { get; set; }

    public int LatencyMs { get; set; }
}

public class ServiceStatusResponse
{
    /// <summary>Ô trả lời thành công ở lần gọi gần nhất (PRIMARY | FALLBACK); null khi chưa có hoặc lần đó thất bại.</summary>
    public string? ActiveSlot { get; set; }

    /// <summary>SUCCEEDED | FAILED; null khi chưa có lần gọi nào kể từ lúc máy chủ khởi động.</summary>
    public string? LastOutcome { get; set; }

    public DateTimeOffset? LastAt { get; set; }

    /// <summary>Mã lỗi ngắn như HTTP_503, TIMEOUT, NETWORK.</summary>
    public string? LastErrorCode { get; set; }
}
