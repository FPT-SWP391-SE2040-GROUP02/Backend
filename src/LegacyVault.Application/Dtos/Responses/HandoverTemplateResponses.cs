using LegacyVault.Application.Services;

namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Bộ mẫu biên bản cho trang Admin: nội dung đang dùng, danh sách trường được chèn và mẫu gốc để khôi phục.</summary>
public class AdminHandoverTemplateResponse
{
    /// <summary>Số phiên bản; null khi đang dùng mẫu gốc trong mã (chưa có bản nào được lưu).</summary>
    public int? Version { get; set; }

    /// <summary>true khi đây là mẫu gốc trong mã.</summary>
    public bool IsDefault { get; set; }

    public List<TemplateParagraph> Accept { get; set; } = new List<TemplateParagraph>();

    public List<TemplateParagraph> Decline { get; set; } = new List<TemplateParagraph>();

    public string DeclineNotice { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public string? CreatedByName { get; set; }

    /// <summary>Các trường được chèn vào mẫu dạng {TÊN}.</summary>
    public List<TemplateTokenInfo> Tokens { get; set; } = new List<TemplateTokenInfo>();

    /// <summary>Mẫu gốc, để nút "Khôi phục mẫu gốc" nạp lại vào trang sửa.</summary>
    public HandoverTemplateContent Default { get; set; } = new HandoverTemplateContent();
}

/// <summary>Một dòng trong lịch sử các lần Admin lưu mẫu.</summary>
public class HandoverTemplateVersionResponse
{
    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedByName { get; set; }

    public string? Note { get; set; }
}
