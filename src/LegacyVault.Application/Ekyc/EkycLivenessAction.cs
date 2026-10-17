namespace LegacyVault.Application.Ekyc;

/// <summary>Một động tác của thử thách camera do dịch vụ eKYC chọn ngẫu nhiên (EKYC-10).</summary>
public sealed class EkycLivenessAction
{
    /// <summary>blink, left hoặc right.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Câu hướng dẫn tiếng Việt hiển thị cho người dùng.</summary>
    public string Label { get; set; } = string.Empty;
}
