namespace LegacyVault.Domain.Enums;

/// <summary>
/// Vai trò của người thực hiện trong nhật ký (SEC-07). Owner, Executor, Beneficiary là quan hệ với kho,
/// không phải vai trò hệ thống (§2); System là job nền, không có tài khoản.
/// </summary>
public enum AuditActorRole
{
    Owner = 0,
    Executor = 1,
    Beneficiary = 2,
    Verifier = 3,
    Admin = 4,
    System = 5,
}
