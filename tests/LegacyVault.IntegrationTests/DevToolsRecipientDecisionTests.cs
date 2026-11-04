using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// Công cụ thử nghiệm của Admin: mốc "hết 7 ngày quyết định của người nhận" (RECIPIENT_DECISION_ENDED).
/// Luật đánh giá mốc kiểm thuần; API kiểm trên SQL Server database tạm. Việc chuyển lượt sang DECISION_EXPIRED
/// sau khi tua do job hết hạn quyết định đảm nhận (đã có HandoverJobTests).
/// </summary>
public sealed class DevToolsRecipientDecisionTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    private static HandoverTurn Turn(HandoverTurnStatus status, DateTimeOffset? decisionDeadline)
    {
        HandoverTurn turn = new HandoverTurn();
        turn.Status = status;
        turn.DecisionDeadline = decisionDeadline;
        return turn;
    }

    [Fact]
    public void RecipientDecision_ReadyTurnBeforeDeadline_IsAvailable()
    {
        List<HandoverTurn> turns = new List<HandoverTurn>();
        turns.Add(Turn(HandoverTurnStatus.Invited, null));
        turns.Add(Turn(HandoverTurnStatus.ReadyToAccept, Now.AddDays(7)));

        Assert.Equal(DevToolsRules.Available, DevToolsRules.EvaluateRecipientDecision(turns, Now).State);
    }

    [Fact]
    public void RecipientDecision_DecisionExpiredOnly_IsPassed()
    {
        List<HandoverTurn> turns = new List<HandoverTurn>();
        turns.Add(Turn(HandoverTurnStatus.DecisionExpired, Now.AddDays(-1)));

        Assert.Equal(DevToolsRules.Passed, DevToolsRules.EvaluateRecipientDecision(turns, Now).State);
    }

    [Fact]
    public void RecipientDecision_NoConfirmedRecipient_IsNotApplicable()
    {
        List<HandoverTurn> none = new List<HandoverTurn>();
        List<HandoverTurn> invitedOnly = new List<HandoverTurn>();
        invitedOnly.Add(Turn(HandoverTurnStatus.Invited, null));

        Assert.Equal(DevToolsRules.NotApplicable, DevToolsRules.EvaluateRecipientDecision(none, Now).State);
        Assert.Equal(DevToolsRules.NotApplicable, DevToolsRules.EvaluateRecipientDecision(invitedOnly, Now).State);
    }

    [Fact]
    public void RecipientDecision_IsListedLast_AndKnown()
    {
        List<string> keys = DevToolsRules.AllMilestones();
        Assert.Equal(DevToolsRules.RecipientDecisionEnded, keys[keys.Count - 1]);
        Assert.True(DevToolsRules.IsKnownMilestone("RECIPIENT_DECISION_ENDED"));
    }

    [SqlFact]
    public async Task Clock_ListsRecipientDecision_AndRefusesTravelWithoutConfirmedRecipient()
    {
        (HttpClient _, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient adminClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(adminClient, factory.Services, admin);

        JsonElement clock = await ApiTestHelpers.ReadJsonAsync(
            await adminClient.GetAsync($"/api/v1/admin/dev-tools/vaults/{vaultId}/clock"));
        JsonElement milestone = FindMilestone(clock, "RECIPIENT_DECISION_ENDED");
        Assert.Equal("NOT_APPLICABLE", milestone.GetProperty("state").GetString());

        HttpResponseMessage travel = await adminClient.PostAsJsonAsync(
            $"/api/v1/admin/dev-tools/vaults/{vaultId}/time-travel", new { milestone = "RECIPIENT_DECISION_ENDED" });
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            travel, HttpStatusCode.Conflict, "DEV_TOOL_NOT_ALLOWED");
        Assert.Equal("RECIPIENT_DECISION_ENDED", problem.GetProperty("milestone").GetString());
    }

    private static JsonElement FindMilestone(JsonElement clock, string key)
    {
        foreach (JsonElement milestone in clock.GetProperty("milestones").EnumerateArray())
        {
            if (milestone.GetProperty("key").GetString() == key)
            {
                return milestone;
            }
        }
        throw new Xunit.Sdk.XunitException("Không thấy mốc " + key);
    }
}
