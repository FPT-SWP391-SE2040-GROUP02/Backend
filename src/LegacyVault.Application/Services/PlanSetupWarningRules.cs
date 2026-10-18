using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>SETUP-10: cảnh báo cấu hình kế hoạch, không phải điều kiện chặn kích hoạt.</summary>
public static class PlanSetupWarningRules
{
    public const string NoBackupExecutor = "NO_BACKUP_EXECUTOR";

    /// <summary>SETUP-10: danh sách đầu vào chỉ gồm Executor đã chấp nhận và đủ điều kiện.</summary>
    public static List<string> GetWarnings(List<Executor> acceptedExecutors)
    {
        List<string> warnings = new List<string>();
        foreach (Executor executor in acceptedExecutors)
        {
            if (executor.Role == ExecutorRole.Backup)
            {
                return warnings;
            }
        }

        warnings.Add(NoBackupExecutor);
        return warnings;
    }
}
