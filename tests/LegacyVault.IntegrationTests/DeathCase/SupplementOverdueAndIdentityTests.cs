using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Hạn bổ sung của Executor (DEATH-01) và cờ IDENTITY_MISMATCH (DEATH-13) trên SQL Server thật.
/// </summary>
public sealed class SupplementOverdueAndIdentityTests : IAsyncLifetime
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

    private async Task<bool> RunJobAsync(Guid caseId)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDeathCaseSupplementJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseSupplementJob>();
            List<Guid> due = await job.FindOverdueCaseIdsAsync(CancellationToken.None);
            if (!due.Contains(caseId))
            {
                return false;
            }
            return await job.ProcessCaseAsync(caseId, CancellationToken.None);
        }
    }

    [SqlFact]
    public async Task Death01_SupplementRequest_HasSevenDayDue_OverdueMarkedOnce_ResubmitClears()
    {
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage requested = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            "Cần ảnh rõ hơn", new[] { "FILES" });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        JsonElement request = (await DeathCaseReviewTestData.GetExecutorCaseAsync(submitted)).GetProperty("supplementRequest");
        DateTimeOffset due = request.GetProperty("dueAt").GetDateTimeOffset();
        Assert.True(due > DateTimeOffset.UtcNow.AddDays(6));
        Assert.True(due < DateTimeOffset.UtcNow.AddDays(8));
        Assert.False(request.GetProperty("overdue").GetBoolean());

        // Chưa tới hạn: job không làm gì
        Assert.False(await RunJobAsync(submitted.CaseId));

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseDecision>().Where(d => d.CaseId == submitted.CaseId)
                .ExecuteUpdateAsync(x => x.SetProperty(d => d.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        });
        Assert.True(await RunJobAsync(submitted.CaseId));
        Assert.False(await RunJobAsync(submitted.CaseId));

        // Quá hạn không tự quyết định gì: hồ sơ vẫn chờ bổ sung, Executor thấy cờ quá hạn
        JsonElement overdue = await DeathCaseReviewTestData.GetExecutorCaseAsync(submitted);
        Assert.Equal("SUPPLEMENT_REQUIRED", overdue.GetProperty("status").GetString());
        Assert.True(overdue.GetProperty("supplementRequest").GetProperty("overdue").GetBoolean());

        // Executor nộp bản mới → hết quá hạn
        HttpResponseMessage revision = await submitted.Executor.Client.PostAsJsonAsync(
            DeathCaseApi.CaseUrl(submitted.CaseId) + "/versions", new { etag = overdue.GetProperty("etag").GetString() });
        Assert.Equal(HttpStatusCode.Created, revision.StatusCode);
        JsonElement draft = await ApiTestHelpers.ReadJsonAsync(revision);
        JsonElement submittedAgain = await DeathCaseApi.SubmitOkAsync(submitted.Executor.Client, draft);
        Assert.Equal("UNDER_REVIEW", submittedAgain.GetProperty("status").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase stored = await db.Set<DeathCase>().SingleAsync(c => c.Id == submitted.CaseId);
            Assert.Null(stored.SupplementOverdueAt);
        });
    }

    [SqlFact]
    public async Task Death13_CitizenIdDiffersFromVerifiedOwner_FlagBlocksApproval()
    {
        await DeathCaseTestData.AddVerifierAsync(factory);
        DeathCaseTestData.OwnerVault owner = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await AddPassedEkycAsync(owner.Owner.Id, "9999");
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, owner.VaultId);

        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(executor.Client, owner.VaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        Guid caseId = submitted.GetProperty("id").GetGuid();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseFlag flag = await db.Set<DeathCaseFlag>().SingleAsync(f => f.CaseId == caseId);
            Assert.Equal(DeathCaseFlagKind.IdentityMismatch, flag.Kind);
            Assert.DoesNotContain(DeathCaseApi.CitizenId, flag.Detail ?? string.Empty);
        });
    }

    [SqlFact]
    public async Task Death13_MatchingOrUnverifiedOwner_NoFlag()
    {
        DeathCaseTestData.OwnerVault verified = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await AddPassedEkycAsync(verified.Owner.Id, DeathCaseApi.CitizenId.Substring(8));
        DeathCaseTestData.OwnerVault unverified = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);

        Guid first = await SubmitAsync(verified);
        Guid second = await SubmitAsync(unverified);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCaseFlag>().CountAsync(f => f.CaseId == first || f.CaseId == second));
        });
    }

    private async Task<Guid> SubmitAsync(DeathCaseTestData.OwnerVault owner)
    {
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, owner.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(executor.Client, owner.VaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);
        return submitted.GetProperty("id").GetGuid();
    }

    private Task AddPassedEkycAsync(Guid userId, string last4)
    {
        return ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            EkycVerification verification = new EkycVerification();
            verification.UserId = userId;
            verification.Status = EkycStatus.Passed;
            verification.CitizenIdLast4 = last4;
            verification.CreatedAt = DateTimeOffset.UtcNow;
            verification.CompletedAt = DateTimeOffset.UtcNow;
            db.Set<EkycVerification>().Add(verification);
            await db.SaveChangesAsync();
        });
    }
}
