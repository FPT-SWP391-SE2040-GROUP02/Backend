using LegacyVault.Application.Services;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Admin lưu một phiên bản mới của bộ mẫu biên bản. Luật kiểm tra nằm ở HandoverTemplateService.</summary>
public class SaveHandoverTemplateRequest
{
    public List<TemplateParagraph> Accept { get; set; } = new List<TemplateParagraph>();

    public List<TemplateParagraph> Decline { get; set; } = new List<TemplateParagraph>();

    public string DeclineNotice { get; set; } = string.Empty;

    /// <summary>Ghi chú về lần sửa, tối đa 300 ký tự; có thể trống.</summary>
    public string? Note { get; set; }
}
