namespace LegacyVault.Application.Services;

/// <summary>Quy tắc ảnh đại diện: dung lượng và loại ảnh nhận theo nội dung thật, không theo đuôi tệp.</summary>
public static class AvatarRules
{
    /// <summary>Dung lượng tối đa của một ảnh đại diện (2 MiB).</summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Nhận loại ảnh theo chữ ký đầu tệp. Chỉ JPEG, PNG, WebP; PDF và mọi loại khác → null.
    /// </summary>
    public static string? DetectImageContentType(byte[] header)
    {
        string? detected = DeathCaseFileRules.DetectContentType(header);
        if (detected == DeathCaseFileRules.Jpeg || detected == DeathCaseFileRules.Png || detected == DeathCaseFileRules.Webp)
        {
            return detected;
        }
        return null;
    }
}
