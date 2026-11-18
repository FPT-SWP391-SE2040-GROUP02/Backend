namespace LegacyVault.Application.Services;

/// <summary>Nội dung đầy đủ của một bộ mẫu: biên bản đồng ý, văn bản từ chối và câu báo khi có người nhận từ chối.</summary>
public sealed class HandoverTemplateContent
{
    public List<TemplateParagraph> Accept { get; set; } = new List<TemplateParagraph>();

    public List<TemplateParagraph> Decline { get; set; } = new List<TemplateParagraph>();

    /// <summary>Câu báo cách xử lý phần tài sản khi có người nhận từ chối; dùng qua trường {CAU_BAO_TU_CHOI} và hiện cho người nhận trước khi chọn.</summary>
    public string DeclineNotice { get; set; } = string.Empty;
}
