using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Biên bản bàn giao và kho mà một Người thực thi được phép làm việc cùng. Kết quả của
/// HandoverAccess.FindCaseForExecutorAsync cho các màn của Người thực thi.
/// </summary>
public class ExecutorCaseAccess
{
    public HandoverRecord Record { get; set; } = null!;

    public Vault Vault { get; set; } = null!;
}
