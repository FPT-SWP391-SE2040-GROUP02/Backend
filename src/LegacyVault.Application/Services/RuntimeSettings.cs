namespace LegacyVault.Application.Services;

/// <summary>
/// Ảnh chụp các tham số Admin đặt trong bảng SystemSettings, cho các luật tĩnh (hạn bàn giao, hạn mức tải lên, khóa PIN...)
/// đọc nhanh mà không cần truy vấn DB. Infrastructure nạp lại định kỳ và ngay sau khi Admin lưu; chưa nạp, thiếu khóa,
/// tham số bị tắt hoặc giá trị sai dạng thì dùng mức gốc (đối số fallback), nên hệ thống không bao giờ chạy với số rỗng.
/// </summary>
public static class RuntimeSettings
{
    private static volatile IReadOnlyDictionary<string, string> values = new Dictionary<string, string>();

    /// <summary>Thay toàn bộ ảnh chụp bằng các tham số đang bật (khóa → giá trị).</summary>
    public static void Replace(IReadOnlyDictionary<string, string> enabledValues)
    {
        values = enabledValues;
    }

    /// <summary>Số nguyên dương Admin đặt; thiếu, tắt hoặc sai dạng → fallback.</summary>
    public static int Int(string key, int fallback)
    {
        string? text;
        int parsed;
        if (values.TryGetValue(key, out text) && int.TryParse(text, out parsed) && parsed > 0)
        {
            return parsed;
        }
        return fallback;
    }

    /// <summary>Như <see cref="Int"/> nhưng cho phép 0 (ví dụ số suất kèm theo gói Free).</summary>
    public static int NonNegativeInt(string key, int fallback)
    {
        string? text;
        int parsed;
        if (values.TryGetValue(key, out text) && int.TryParse(text, out parsed) && parsed >= 0)
        {
            return parsed;
        }
        return fallback;
    }

    public static bool Bool(string key, bool fallback)
    {
        string? text;
        bool parsed;
        if (values.TryGetValue(key, out text) && bool.TryParse(text, out parsed))
        {
            return parsed;
        }
        return fallback;
    }

    public static TimeSpan Days(string key, int fallbackDays)
    {
        return TimeSpan.FromDays(Int(key, fallbackDays));
    }

    public static TimeSpan Hours(string key, int fallbackHours)
    {
        return TimeSpan.FromHours(Int(key, fallbackHours));
    }

    public static TimeSpan Minutes(string key, int fallbackMinutes)
    {
        return TimeSpan.FromMinutes(Int(key, fallbackMinutes));
    }
}
