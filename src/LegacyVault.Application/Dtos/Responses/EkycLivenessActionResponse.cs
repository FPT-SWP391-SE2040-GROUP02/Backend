namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một động tác của thử thách camera.</summary>
public class EkycLivenessActionResponse
{
    /// <summary>blink, left hoặc right.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Câu hướng dẫn tiếng Việt.</summary>
    public string Label { get; set; } = string.Empty;
}
