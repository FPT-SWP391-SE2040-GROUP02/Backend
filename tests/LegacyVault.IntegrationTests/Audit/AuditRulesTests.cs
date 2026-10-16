using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>Luật dựng dòng nhật ký hoạt động (không cần DB).</summary>
public sealed class AuditRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sec13_OwnerAction_FillsOwnerRoleAndSucceeded()
    {
        Guid vaultId = Guid.NewGuid();
        Guid ownerId = Guid.NewGuid();
        Guid containerId = Guid.NewGuid();

        AuditLog log = AuditRules.OwnerAction(
            vaultId, ownerId, AuditAction.ContainerCreated, AuditTargetType.Container, containerId, 3, Now);

        Assert.Equal(vaultId, log.VaultId);
        Assert.Equal(ownerId, log.ActorUserId);
        Assert.Equal(AuditActorRole.Owner, log.ActorRole);
        Assert.Equal(AuditAction.ContainerCreated, log.Action);
        Assert.Equal(AuditTargetType.Container, log.TargetType);
        Assert.Equal(containerId, log.TargetId);
        Assert.Equal(3, log.TargetVersion);
        Assert.Equal(AuditOutcome.Succeeded, log.Outcome);
        Assert.Null(log.Reason);
        Assert.Equal(Now, log.At);
    }

    [Fact]
    public void Sec07_Create_SystemActionHasNoActor()
    {
        AuditLog log = AuditRules.Create(
            Guid.NewGuid(), null, AuditActorRole.System, AuditAction.PlanDeactivated, AuditTargetType.Plan,
            null, null, AuditOutcome.Succeeded, "  Hết gói READY  ", Now);

        Assert.Null(log.ActorUserId);
        Assert.Equal(AuditActorRole.System, log.ActorRole);
        Assert.Equal("Hết gói READY", log.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeReason_BlankBecomesNull(string? reason)
    {
        Assert.Null(AuditRules.NormalizeReason(reason));
    }

    [Fact]
    public void NormalizeReason_LongTextIsCutToColumnLength()
    {
        string reason = new string('a', AuditRules.MaxReasonLength + 20);

        string? normalized = AuditRules.NormalizeReason(reason);

        Assert.NotNull(normalized);
        Assert.Equal(AuditRules.MaxReasonLength, normalized.Length);
    }
}
