namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Admin lưu chữ cho một loại thư. Trường trống hoặc null là dùng lại chữ gốc.</summary>
public class SaveEmailTemplateRequest
{
    public bool Enabled { get; set; } = true;

    public string? Subject { get; set; }

    public string? Title { get; set; }

    public string? Body { get; set; }

    public string? ButtonText { get; set; }

    public string? Note { get; set; }
}
