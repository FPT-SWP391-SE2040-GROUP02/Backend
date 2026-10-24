using System.Net;
using System.Net.Http.Json;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// API tiến độ bàn giao trên SQL Server thật: quyền Executor, truy vấn gom nhóm lần tải và 4 số cuối CCCD.
/// Lượt, tài sản đã ghim, quyền tải và nhật ký tải được chèn thẳng DB (không chạy cả luồng duyệt).
/// </summary>
public sealed class HandoverProgressApiTests : IAsyncLifetime
{
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

    [SqlFact]
    public async Task Executor_SeesTurnsAndOnlySuccessfulDownloads()
    {
        // Chuẩn bị: hồ sơ đã duyệt, một lượt đã đồng ý có hai tài sản
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        Guid caseId = await AddApprovedCaseAsync(vault, executor.User.Id);
        Guid beneficiaryId = await AddBeneficiaryAsync(vault.VaultId, "4321");
        SeededTurn seeded = await AddCommittedTurnAsync(vault, beneficiaryId);
        DateTimeOffset firstAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        await AddAccessLogAsync(seeded, seeded.FirstItemId, HandoverAccessOutcome.Denied, firstAt.AddMinutes(-5));
        await AddAccessLogAsync(seeded, seeded.FirstItemId, HandoverAccessOutcome.Succeeded, firstAt);
        await AddAccessLogAsync(seeded, seeded.FirstItemId, HandoverAccessOutcome.Succeeded, firstAt.AddMinutes(10));
        await AddAccessLogAsync(seeded, seeded.SecondItemId, HandoverAccessOutcome.IntegrityFailed, firstAt);

        // Hành động
        HttpResponseMessage response = await executor.Client.GetAsync($"/api/v1/executor/death-cases/{caseId}/handover-progress");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HandoverProgressResponse? body = await response.Content.ReadFromJsonAsync<HandoverProgressResponse>();
        Assert.NotNull(body);
        Assert.True(body.HandoverStarted);
        Assert.Equal(1, body.Summary.Accepted);
        HandoverProgressTurnResponse turn = Assert.Single(body.Turns);
        Assert.Equal("Gói thử", turn.ContainerName);
        Assert.Equal("4321", turn.BeneficiaryCitizenIdLast4);
        Assert.Equal("HANDOVER_COMMITTED", turn.Status);
        Assert.Equal("ACTIVE", turn.GrantStatus);
        Assert.Equal(2, turn.TotalItems);
        Assert.Equal(1, turn.DownloadedItems);
        Assert.Equal("Tài sản một", turn.Items[0].Name);
        Assert.True(turn.Items[0].Downloaded);
        Assert.NotNull(turn.Items[0].FirstDownloadedAt);
        Assert.True(Math.Abs((turn.Items[0].FirstDownloadedAt!.Value - firstAt).TotalSeconds) < 1);
        Assert.False(turn.Items[1].Downloaded);

        string json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task OwnerAndStranger_GetNotFound()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        Guid caseId = await AddApprovedCaseAsync(vault, executor.User.Id);
        User stranger = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient strangerClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(strangerClient, factory.Services, stranger);
        string url = $"/api/v1/executor/death-cases/{caseId}/handover-progress";

        HttpResponseMessage ownerResponse = await vault.Client.GetAsync(url);
        HttpResponseMessage strangerResponse = await strangerClient.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, ownerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
        string body = await strangerResponse.Content.ReadAsStringAsync();
        Assert.Contains("DEATH_CASE_NOT_FOUND", body, StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task SuspendedExecutor_GetsNotFound()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        User user = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await DeathCaseTestData.AddExecutorAsync(factory, vault.VaultId, user.Id, ExecutorStatus.Suspended, ExecutorRole.Primary);
        Guid caseId = await AddApprovedCaseAsync(vault, user.Id);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);

        HttpResponseMessage response = await client.GetAsync($"/api/v1/executor/death-cases/{caseId}/handover-progress");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SqlFact]
    public async Task CaseUnderReview_ReturnsNotStarted()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        DeathCase deathCase = DeathCaseTestData.NewDraftCase(vault, executor.User.Id);
        deathCase.Status = DeathCaseStatus.UnderReview;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<DeathCase>().Add(deathCase);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await executor.Client.GetAsync($"/api/v1/executor/death-cases/{deathCase.Id}/handover-progress");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HandoverProgressResponse? body = await response.Content.ReadFromJsonAsync<HandoverProgressResponse>();
        Assert.NotNull(body);
        Assert.False(body.HandoverStarted);
        Assert.Empty(body.Turns);
    }

    private async Task<Guid> AddApprovedCaseAsync(DeathCaseTestData.OwnerVault vault, Guid executorUserId)
    {
        DeathCase deathCase = DeathCaseTestData.NewDraftCase(vault, executorUserId);
        deathCase.Status = DeathCaseStatus.ApprovedForHandover;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<DeathCase>().Add(deathCase);
            await db.SaveChangesAsync();
        });
        return deathCase.Id;
    }

    private async Task<Guid> AddBeneficiaryAsync(Guid vaultId, string last4)
    {
        Beneficiary beneficiary = new Beneficiary();
        beneficiary.VaultId = vaultId;
        beneficiary.CitizenIdLast4 = last4;
        beneficiary.EncryptionKeyId = "k1";
        beneficiary.CreatedAt = DateTimeOffset.UtcNow;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<Beneficiary>().Add(beneficiary);
            await db.SaveChangesAsync();
        });
        return beneficiary.Id;
    }

    /// <summary>Lượt đã đồng ý kèm hai tài sản đã ghim và quyền tải đang hiệu lực.</summary>
    private async Task<SeededTurn> AddCommittedTurnAsync(DeathCaseTestData.OwnerVault vault, Guid beneficiaryId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HandoverTurn turn = new HandoverTurn();
        turn.VaultId = vault.VaultId;
        turn.PlanId = vault.PlanId;
        turn.ContainerId = Guid.NewGuid();
        turn.ContainerName = "Gói thử";
        turn.DesignationId = Guid.NewGuid();
        turn.BeneficiaryId = beneficiaryId;
        turn.Status = HandoverTurnStatus.HandoverCommitted;
        turn.InvitationTokenHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        turn.InvitedAt = now.AddDays(-2);
        turn.ResponseDeadline = now.AddDays(3);
        turn.ClaimantUserId = Guid.NewGuid();
        turn.ClaimedAt = now.AddDays(-1);
        turn.DecisionDeadline = now.AddDays(6);
        turn.DecidedAt = now.AddHours(-2);
        turn.CreatedAt = now.AddDays(-2);

        HandoverSnapshotItem first = NewItem(turn, "Tài sản một", 0);
        HandoverSnapshotItem second = NewItem(turn, "Tài sản hai", 1);
        turn.Items.Add(first);
        turn.Items.Add(second);

        HandoverGrant grant = new HandoverGrant();
        grant.TurnId = turn.Id;
        grant.UserId = turn.ClaimantUserId.Value;
        grant.Status = HandoverGrantStatus.Active;
        grant.GrantedAt = now.AddHours(-2);
        grant.ExpiresAt = now.AddDays(30);
        turn.Grant = grant;

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<HandoverTurn>().Add(turn);
            await db.SaveChangesAsync();
        });

        SeededTurn seeded = new SeededTurn();
        seeded.GrantId = grant.Id;
        seeded.UserId = grant.UserId;
        seeded.FirstItemId = first.Id;
        seeded.SecondItemId = second.Id;
        return seeded;
    }

    private static HandoverSnapshotItem NewItem(HandoverTurn turn, string name, int position)
    {
        HandoverSnapshotItem item = new HandoverSnapshotItem();
        item.TurnId = turn.Id;
        item.AssetId = Guid.NewGuid();
        item.Name = name;
        item.Kind = AssetKind.File;
        item.FileName = "tep.pdf";
        item.ContentType = "application/pdf";
        item.SizeBytes = 100;
        item.Position = position;
        return item;
    }

    private async Task AddAccessLogAsync(SeededTurn seeded, Guid itemId, HandoverAccessOutcome outcome, DateTimeOffset at)
    {
        HandoverAccessLog log = new HandoverAccessLog();
        log.GrantId = seeded.GrantId;
        log.SnapshotItemId = itemId;
        log.UserId = seeded.UserId;
        log.Outcome = outcome;
        log.At = at;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<HandoverAccessLog>().Add(log);
            await db.SaveChangesAsync();
        });
    }

    private sealed class SeededTurn
    {
        public Guid GrantId { get; set; }

        public Guid UserId { get; set; }

        public Guid FirstItemId { get; set; }

        public Guid SecondItemId { get; set; }
    }
}
