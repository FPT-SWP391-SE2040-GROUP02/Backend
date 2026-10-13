namespace LegacyVault.Domain.Enums;

/// <summary>Hạng dịch vụ cấp kho (SRS §8.1).</summary>
public enum ServiceTier
{
    /// <summary>20 MiB, 0 suất người nhận; chỉ được tạo Container rỗng (SETUP-17).</summary>
    Free = 0,
    /// <summary>200 MiB, 2 suất.</summary>
    Xs = 1,
    /// <summary>500 MiB, 5 suất.</summary>
    XsMax = 2,
}
