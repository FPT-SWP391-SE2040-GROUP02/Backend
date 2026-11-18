namespace LegacyVault.Application.Services;

/// <summary>Một trường Admin được chèn vào mẫu, kèm nhãn và ví dụ để hiện ở trang sửa mẫu.</summary>
public sealed class TemplateTokenInfo
{
    public string Token { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Example { get; set; } = string.Empty;
}
