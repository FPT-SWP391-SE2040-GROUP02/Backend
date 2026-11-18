namespace LegacyVault.Application.Services;

/// <summary>Mẫu đã chọn cho một biên bản: khóa phiên bản ghi vào biên bản và nội dung của phiên bản đó.</summary>
public sealed class ResolvedHandoverTemplate
{
    public string VersionKey { get; set; } = string.Empty;

    public HandoverTemplateContent Content { get; set; } = new HandoverTemplateContent();
}
