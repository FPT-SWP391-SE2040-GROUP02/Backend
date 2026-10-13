namespace LegacyVault.Domain.Enums;

/// <summary>
/// Vai trò hệ thống, ghi vào token đăng nhập.
/// </summary>
/// <remarks>
/// ROLE-01: ai đăng ký (app hoặc Google) cũng là User; Verifier/Admin chỉ được cấp qua quản trị.
/// Owner, Executor, Beneficiary KHÔNG phải vai trò mà là quan hệ với từng kho
/// (một người có thể là Owner kho A và Beneficiary kho B, SETUP-15) — kiểm tra trong service (ROLE-02).
/// </remarks>
public enum UserRole
{
    User = 0,
    Verifier = 1,
    Admin = 2,
}
