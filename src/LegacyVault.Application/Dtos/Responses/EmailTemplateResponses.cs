namespace LegacyVault.Application.Dtos.Responses;

public class EmailTemplateTokenResponse
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public class EmailTemplateFieldsResponse
{
    public string Subject { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string ButtonText { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;
}

/// <summary>Một loại thư thông báo: mô tả, chữ gốc và chữ Admin đã đặt (null: giữ chữ gốc).</summary>
public class EmailTemplateResponse
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool CanDisable { get; set; }

    public bool EditableBody { get; set; }

    public bool Enabled { get; set; } = true;

    public List<EmailTemplateTokenResponse> Tokens { get; set; } = new List<EmailTemplateTokenResponse>();

    /// <summary>Chữ gốc trong mã, biến {TEN_...} giữ nguyên.</summary>
    public EmailTemplateFieldsResponse Default { get; set; } = new EmailTemplateFieldsResponse();

    /// <summary>Chữ Admin đã đặt; trường null là giữ chữ gốc.</summary>
    public EmailTemplateCustomResponse Custom { get; set; } = new EmailTemplateCustomResponse();

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? UpdatedByName { get; set; }
}

public class EmailTemplateCustomResponse
{
    public string? Subject { get; set; }

    public string? Title { get; set; }

    public string? Body { get; set; }

    public string? ButtonText { get; set; }

    public string? Note { get; set; }
}
