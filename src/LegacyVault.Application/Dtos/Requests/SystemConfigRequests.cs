namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Lưu cấu hình một ô của dịch vụ ngoài. Settings gửi đủ mọi trường thường; Secrets chỉ cần gửi trường muốn đổi
/// (bỏ trống hoặc null nghĩa là giữ nguyên). Mọi giá trị đều là chuỗi, số cũng vậy.
/// </summary>
public class UpdateSystemConfigRequest
{
    public Dictionary<string, string?> Settings { get; set; } = new Dictionary<string, string?>();

    public Dictionary<string, string?>? Secrets { get; set; }

    /// <summary>Bật/tắt ô dự phòng. Ô chính luôn bật nên gửi false cho ô chính là lỗi.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Lý do thay đổi, bắt buộc, tối đa 500 ký tự; kiểm bằng SystemConfigValidator.RequireReason.</summary>
    public string? Reason { get; set; }

    /// <summary>Version của ô lúc Admin mở form (0 khi ô chưa có dòng trong DB). Lệch thì 409 EDIT_CONFLICT.</summary>
    public int Version { get; set; }
}

/// <summary>Thử kết nối bằng giá trị nháp: cùng dạng settings/secrets với lúc lưu, nhưng không có lý do và không lưu gì.</summary>
public class TestSystemConfigRequest
{
    public Dictionary<string, string?> Settings { get; set; } = new Dictionary<string, string?>();

    public Dictionary<string, string?>? Secrets { get; set; }
}

public class ResetSystemConfigRequest
{
    public string? Reason { get; set; }
}
