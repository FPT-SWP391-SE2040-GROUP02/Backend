namespace LegacyVault.Domain.Enums;

/// <summary>Hạng dịch vụ cấp kho (SRS §8.1).</summary>
public enum ServiceTier
{
    /// <summary>20 MiB, 0 suất người nhận; chưa được gán tài sản cho người nhận.</summary>
    Free = 0,
    /// <summary>200 MiB, 2 suất.</summary>
    Xs = 1,
    /// <summary>500 MiB, 5 suất.</summary>
    XsMax = 2,
    /// <summary>Gói do Admin tạo thêm; mã gói cụ thể nằm ở <c>PlanCode</c> của quyền dịch vụ và đơn thanh toán.</summary>
    Custom = 3,
}
