namespace LegacyVault.Domain.Enums;

/// <summary>
/// Mã gói dịch vụ. Ba gói gốc có mã cố định và khớp enum <see cref="ServiceTier"/>; gói Admin tạo thêm có mã riêng
/// (ví dụ "GOLD") và <see cref="ServiceTier.Custom"/>. Dòng cũ chưa có PlanCode được suy ra từ hạng.
/// </summary>
public static class PlanCodes
{
    public const string Free = "FREE";
    public const string Xs = "XS";
    public const string XsMax = "XS_MAX";

    /// <summary>Mã gói thực của một quyền dịch vụ hoặc đơn: PlanCode nếu có, không thì suy ra từ hạng gốc.</summary>
    public static string Resolve(ServiceTier tier, string? planCode)
    {
        if (!string.IsNullOrWhiteSpace(planCode))
        {
            return planCode;
        }
        if (tier == ServiceTier.Xs)
        {
            return Xs;
        }
        if (tier == ServiceTier.XsMax)
        {
            return XsMax;
        }
        return Free;
    }

    /// <summary>Hạng enum tương ứng với một mã gói: gói gốc giữ hạng riêng, mọi gói khác là Custom.</summary>
    public static ServiceTier TierOf(string planCode)
    {
        if (planCode == Xs)
        {
            return ServiceTier.Xs;
        }
        if (planCode == XsMax)
        {
            return ServiceTier.XsMax;
        }
        if (planCode == Free)
        {
            return ServiceTier.Free;
        }
        return ServiceTier.Custom;
    }
}
