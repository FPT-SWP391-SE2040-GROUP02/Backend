using System.Net;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Executor hủy hồ sơ chứng tử còn nháp qua HTTP trên SQL Server thật: xóa hồ sơ và trang đã tải, kho trở lại như
/// chưa báo tin (mở lại được, điểm danh thay chủ kho được). Hồ sơ đã gửi thì không hủy được.
/// </summary>
public sealed class DeathCaseCancelDraftFlowTests : IAsyncLifetime
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

    private static string DraftUrl(Guid caseId)
    {
        return DeathCaseApi.CaseUrl(caseId) + "/draft";
    }

    [SqlFact]
    public async Task CancelDraft_RemovesCaseAndFiles_AndVaultCanOpenAgain()
    {
        // Chuẩn bị: hồ sơ nháp đã khai đủ và có một trang
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        Guid caseId = prepared.GetProperty("id").GetGuid();
        Assert.Single(Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories));

        // Hành động
        HttpResponseMessage response = await executor.Client.DeleteAsync(DraftUrl(caseId));

        // Kiểm tra: 204, hồ sơ, bản nháp, trang và nội dung đã lưu đều bị xóa; nhật ký ghi lại việc hủy
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.False(await db.Set<DeathCase>().AnyAsync(c => c.Id == caseId));
            Assert.False(await db.Set<DeathCaseVersion>().AnyAsync(v => v.CaseId == caseId));
            HandoverPlan plan = await db.HandoverPlans.SingleAsync(p => p.Id == vault.PlanId);
            Assert.Equal(PlanStatus.Active, plan.Status);
            AuditLog log = await db.AuditLogs.SingleAsync(
                l => l.VaultId == vault.VaultId && l.Action == AuditAction.DeathCaseDraftCancelled);
            Assert.Equal(AuditActorRole.Executor, log.ActorRole);
            Assert.Equal(executor.User.Id, log.ActorUserId);
            Assert.Equal(caseId, log.TargetId);
        });

        JsonElement vaults = await ApiTestHelpers.ReadJsonAsync(await executor.Client.GetAsync("/api/v1/executor/vaults"));
        Assert.Equal(JsonValueKind.Null, vaults[0].GetProperty("openDeathCaseId").ValueKind);
        HttpResponseMessage gone = await executor.Client.GetAsync(DeathCaseApi.CaseUrl(caseId));
        await ApiTestHelpers.AssertProblemAsync(gone, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        JsonElement reopened = await DeathCaseApi.OpenAsync(executor.Client, vault.VaultId);
        Assert.NotEqual(caseId, reopened.GetProperty("id").GetGuid());
    }

    [SqlFact]
    public async Task CancelDraft_ThenExecutorCanCheckInForOverdueOwner()
    {
        // Chuẩn bị: chủ kho quá hạn, Executor lỡ mở hồ sơ nên bị chặn điểm danh thay
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-40), null);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.AwaitingCheck);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement opened = await DeathCaseApi.OpenAsync(executor.Client, vault.VaultId);
        string checkInUrl = "/api/v1/executor/vaults/" + vault.VaultId + "/check-in";
        HttpResponseMessage blocked = await executor.Client.PostAsync(checkInUrl, null);
        await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.Conflict, "CHECKIN_NOT_ALLOWED");

        // Hành động
        HttpResponseMessage cancel = await executor.Client.DeleteAsync(DraftUrl(opened.GetProperty("id").GetGuid()));
        HttpResponseMessage checkIn = await executor.Client.PostAsync(checkInUrl, null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.OK, checkIn.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverPlan plan = await db.HandoverPlans.SingleAsync(p => p.Id == vault.PlanId);
            Assert.Equal(PlanStatus.Active, plan.Status);
        });
    }

    [SqlFact]
    public async Task CancelSubmittedCase_Returns409VersionLocked()
    {
        // Chuẩn bị: hồ sơ đã gửi, kế hoạch đã khóa
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);
        Guid caseId = submitted.GetProperty("id").GetGuid();

        // Hành động
        HttpResponseMessage response = await executor.Client.DeleteAsync(DraftUrl(caseId));

        // Kiểm tra: hồ sơ, trang và kế hoạch giữ nguyên
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("VERSION_LOCKED", problem.GetProperty("reason").GetString());
        Assert.Single(Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase deathCase = await db.Set<DeathCase>().SingleAsync(c => c.Id == caseId);
            Assert.Equal(DeathCaseStatus.UnderReview, deathCase.Status);
            HandoverPlan plan = await db.HandoverPlans.SingleAsync(p => p.Id == vault.PlanId);
            Assert.Equal(PlanStatus.DeathCaseOpen, plan.Status);
            Assert.False(await db.AuditLogs.AnyAsync(l => l.Action == AuditAction.DeathCaseDraftCancelled));
        });
    }

    [SqlFact]
    public async Task OwnerAndStranger_Return404()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement opened = await DeathCaseApi.OpenAsync(executor.Client, vault.VaultId);
        Guid caseId = opened.GetProperty("id").GetGuid();
        User stranger = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient strangerClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(strangerClient, factory.Services, stranger);

        // Hành động
        HttpResponseMessage byOwner = await vault.Client.DeleteAsync(DraftUrl(caseId));
        HttpResponseMessage byStranger = await strangerClient.DeleteAsync(DraftUrl(caseId));

        // Kiểm tra: không lộ hồ sơ có tồn tại hay không (SEC-04), hồ sơ còn nguyên
        await ApiTestHelpers.AssertProblemAsync(byOwner, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(byStranger, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.True(await db.Set<DeathCase>().AnyAsync(c => c.Id == caseId));
        });
    }
}
