using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos;

public sealed record SystemSettingResponse(
    Guid Id,
    string Key,
    string Value,
    string DataType,
    string Category,
    string Description,
    bool IsEnabled,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy);

public sealed class UpdateSystemSettingRequest
{
    public UpdateSystemSettingRequest() { }

    public UpdateSystemSettingRequest(string value, bool? isEnabled = null, string? reason = null)
    {
        Value = value;
        IsEnabled = isEnabled;
        Reason = reason;
    }

    [Required, MaxLength(2000)]
    public string Value { get; set; } = "";

    public bool? IsEnabled { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public sealed class BatchUpdateSystemSettingsRequest
{
    public List<BatchUpdateSystemSettingItem> Items { get; set; } = [];

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public sealed class BatchUpdateSystemSettingItem
{
    [Required, MaxLength(100)]
    public string Key { get; set; } = "";

    [Required, MaxLength(2000)]
    public string Value { get; set; } = "";

    public bool? IsEnabled { get; set; }
}

public sealed class CreateSystemSettingRequest
{
    [Required, MaxLength(100)]
    public string Key { get; set; } = "";

    [Required, MaxLength(2000)]
    public string Value { get; set; } = "";

    [MaxLength(20)]
    public string? DataType { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool? IsEnabled { get; set; }
}
