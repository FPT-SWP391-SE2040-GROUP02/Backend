namespace LegacyVault.Application.Services;

/// <summary>
/// Giới hạn số lần Executor điểm danh thay chủ kho liên tiếp. Bộ đếm về 0 khi chủ kho tự điểm danh
/// (kỳ mới do chủ kho mở có StartedByExecutor = false), nên không cần cột đếm riêng.
/// </summary>
public static class ProxyCheckInRules
{
    /// <summary>Khóa tham số Admin đặt trong SystemSettings.</summary>
    public const string LimitSettingKey = "CheckIn.ExecutorProxyLimit";

    /// <summary>Mức gốc khi Admin chưa đặt.</summary>
    public const int DefaultLimit = 3;

    /// <summary>Số lần điểm danh thay liên tiếp tối đa hiện hành.</summary>
    public static int Limit()
    {
        return Math.Clamp(RuntimeSettings.Int(LimitSettingKey, DefaultLimit), 1, 20);
    }

    /// <summary>Số lượt còn lại, không âm.</summary>
    public static int Left(int used)
    {
        int left = Limit() - used;
        if (left < 0)
        {
            return 0;
        }
        return left;
    }
}
