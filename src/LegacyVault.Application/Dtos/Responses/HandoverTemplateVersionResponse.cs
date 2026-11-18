namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một dòng trong lịch sử các lần Admin lưu mẫu.</summary>
public class HandoverTemplateVersionResponse
{
    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedByName { get; set; }

    public string? Note { get; set; }
}
