using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests;

public sealed class PlanSetupWarningRulesTests
{
    [Fact]
    public void SETUP10_GetWarnings_EmptyAccepted_ReturnsNoBackupExecutor()
    {
        // Chuẩn bị
        List<Executor> acceptedExecutors = new List<Executor>();

        // Hành động
        List<string> warnings = PlanSetupWarningRules.GetWarnings(acceptedExecutors);

        // Kiểm tra
        Assert.Single(warnings);
        Assert.Equal("NO_BACKUP_EXECUTOR", warnings[0]);
    }

    [Fact]
    public void SETUP10_GetWarnings_PrimaryOnly_ReturnsNoBackupExecutor()
    {
        // Chuẩn bị
        List<Executor> acceptedExecutors = new List<Executor>
        {
            new Executor { Role = ExecutorRole.Primary, Status = ExecutorStatus.Accepted },
        };

        // Hành động
        List<string> warnings = PlanSetupWarningRules.GetWarnings(acceptedExecutors);

        // Kiểm tra
        Assert.Single(warnings);
        Assert.Equal("NO_BACKUP_EXECUTOR", warnings[0]);
    }

    [Fact]
    public void SETUP10_GetWarnings_WithBackup_ReturnsEmpty()
    {
        // Chuẩn bị
        List<Executor> acceptedExecutors = new List<Executor>
        {
            new Executor { Role = ExecutorRole.Primary, Status = ExecutorStatus.Accepted },
            new Executor { Role = ExecutorRole.Backup, Status = ExecutorStatus.Accepted },
        };

        // Hành động
        List<string> warnings = PlanSetupWarningRules.GetWarnings(acceptedExecutors);

        // Kiểm tra
        Assert.Empty(warnings);
    }
}
