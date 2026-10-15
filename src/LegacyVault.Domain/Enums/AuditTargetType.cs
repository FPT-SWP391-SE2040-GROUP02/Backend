namespace LegacyVault.Domain.Enums;

/// <summary>Loại đối tượng bị tác động, đi cùng TargetId trong nhật ký (SEC-07, ADMIN-06 lọc theo đối tượng).</summary>
public enum AuditTargetType
{
    Vault = 0,
    Plan = 1,
    Asset = 2,
    Container = 3,
    Beneficiary = 4,
    CheckIn = 5,
}
