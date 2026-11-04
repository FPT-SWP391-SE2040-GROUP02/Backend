namespace LegacyVault.Application.Services;

/// <summary>Hằng số dung lượng (QUOTA-01: 1 MiB = 1.048.576 byte).</summary>
public static class StorageLimits
{
    public const long MiB = 1_048_576;

    /// <summary>Quota kho hạng Free (§8.1).</summary>
    public static long FreeQuotaBytes => RuntimeSettings.NonNegativeInt("Plans.Free.QuotaMb", 20) * MiB;

    /// <summary>
    /// Trần một lần tải lên = quota hạng cao nhất (XS Max 500 MiB). Quota còn lại của kho vẫn được kiểm tra riêng.
    /// Controller dùng số này cho giới hạn request body.
    /// </summary>
    public const long MaxUploadBytes = 1000 * MiB;

    /// <summary>Trần một lần tải lên do Admin đặt (Limits.MaxUploadBytesMb); không vượt <see cref="MaxUploadBytes"/>.</summary>
    public static long ConfiguredMaxUploadBytes => Math.Min(MaxUploadBytes, RuntimeSettings.Int("Limits.MaxUploadBytesMb", 500) * MiB);
}
