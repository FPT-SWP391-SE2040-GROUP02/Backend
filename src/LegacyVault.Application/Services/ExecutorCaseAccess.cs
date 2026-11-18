using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Hồ sơ chứng tử và kho mà một Người thực thi được phép làm việc cùng. Kết quả của
/// HandoverAccess.FindCaseForExecutorAsync cho các màn của Người thực thi.
/// </summary>
public class ExecutorCaseAccess
{
    public DeathCase DeathCase { get; set; } = null!;

    public Vault Vault { get; set; } = null!;
}
