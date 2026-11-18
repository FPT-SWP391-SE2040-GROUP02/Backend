namespace LegacyVault.Application.Services;

/// <summary>Một đoạn của mẫu biên bản: chữ (có thể chứa trường như {HO_TEN_NGUOI_NHAN}), căn lề và có đậm hay không.</summary>
public class TemplateParagraph
{
    public const string AlignLeft = "left";
    public const string AlignCenter = "center";
    public const string AlignJustify = "justify";

    public string Text { get; set; } = string.Empty;

    /// <summary>"left", "center" hoặc "justify".</summary>
    public string Align { get; set; } = AlignJustify;

    public bool Bold { get; set; }

    public TemplateParagraph()
    {
    }

    public TemplateParagraph(string text, string align, bool bold)
    {
        Text = text;
        Align = align;
        Bold = bold;
    }
}
