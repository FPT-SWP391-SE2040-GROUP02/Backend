namespace LegacyVault.Application.Services;

/// <summary>Luật của công cụ thử nghiệm của Admin (chỉ có ở Development).</summary>
public static class DevToolsRules
{
    /// <summary>Dải an toàn cho ngưỡng so khớp khuôn mặt eKYC khi Admin đổi bằng DevTools.</summary>
    public const double EkycThresholdMin = 0.20;

    public const double EkycThresholdMax = 0.60;

    /// <summary>Ngưỡng nằm trong dải an toàn [EkycThresholdMin; EkycThresholdMax], không phải NaN/Infinity.</summary>
    public static bool IsSafeEkycThreshold(double threshold)
    {
        return !double.IsNaN(threshold)
            && !double.IsInfinity(threshold)
            && threshold >= EkycThresholdMin
            && threshold <= EkycThresholdMax;
    }
}
