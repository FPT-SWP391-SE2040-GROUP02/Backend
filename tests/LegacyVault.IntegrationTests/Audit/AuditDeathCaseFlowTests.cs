using System.Net;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>
/// Nhật ký thao tác của Executor và Verifier trên hồ sơ chứng tử (SEC-07, SEC-13, AC-80) trên SQL Server thật.
/// Đọc thẳng bảng AuditLogs; dữ liệu hồ sơ dựng bằng DeathCaseReviewTestData / DeathCaseApi.
/// </summary>
public sealed class AuditDeathCaseFlowTests : IAsyncLifetime
{
    private const string FreeTextReason = "Ảnh giấy chứng tử bị mờ, không đọc được số";

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
    public async Task SEC07_ExecutorOpen_WritesOneRow_RepeatOpenDoesNotAdd()
    {
        // Chuẩn bị: kho có kế hoạch đang chạy và một Executor đang hiệu lực
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        // Hành động: mở hồ sơ hai lần
        JsonElement opened = await DeathCaseApi.OpenAsync(executor.Client, vault.VaultId);
        HttpResponseMessage repeat = await executor.Client.PostAsync(DeathCaseApi.OpenUrl(vault.VaultId), null);

        // Kiểm tra: lần hai trả hồ sơ cũ, nhật ký vẫn đúng một dòng
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Guid caseId = opened.GetProperty("id").GetGuid();
        List<AuditLog> rows = await AuditTestData.ListForVaultAsync(factory, vault.VaultId, AuditAction.DeathCaseOpened);
        AuditLog row = Assert.Single(rows);
        Assert.Equal(AuditActorRole.Executor, row.ActorRole);
        Assert.Equal(executor.User.Id, row.ActorUserId);
        Assert.Equal(AuditTargetType.DeathCase, row.TargetType);
        Assert.Equal(caseId, row.TargetId);
        Assert.Equal(1, row.TargetVersion);
        Assert.Equal(AuditOutcome.Succeeded, row.Outcome);
        Assert.Null(row.Reason);
    }

    [SqlFact]
    public async Task SEC07_ExecutorSubmitAndNewVersion_WriteRowsWithVersionNo()
    {
        // Chuẩn bị: bản 1 đã gửi, Verifier yêu cầu bổ sung
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid vaultId = submitted.Vault.VaultId;
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        HttpResponseMessage supplement = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            FreeTextReason, new[] { "FILES" });
        Assert.Equal(HttpStatusCode.OK, supplement.StatusCode);

        // Hành động: Executor tạo bản 2 rồi gửi bản 2
        await DeathCaseReviewTestData.ResubmitAsync(submitted);

        // Kiểm tra
        List<AuditLog> submittedRows = await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.DeathCaseSubmitted);
        Assert.Equal(2, submittedRows.Count);
        Assert.Equal(1, submittedRows[0].TargetVersion);
        Assert.Equal(2, submittedRows[1].TargetVersion);
        foreach (AuditLog row in submittedRows)
        {
            Assert.Equal(AuditActorRole.Executor, row.ActorRole);
            Assert.Equal(submitted.Executor.User.Id, row.ActorUserId);
            Assert.Equal(submitted.CaseId, row.TargetId);
        }
        List<AuditLog> versionRows = await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.DeathCaseVersionCreated);
        AuditLog versionRow = Assert.Single(versionRows);
        Assert.Equal(2, versionRow.TargetVersion);
        Assert.Equal(AuditActorRole.Executor, versionRow.ActorRole);
    }

    [SqlFact]
    public async Task SEC07_VerifierSupplementRequest_ReasonHoldsOnlyRequestedParts()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Hành động: requestedParts sai thứ tự để kiểm chuỗi đã chuẩn hóa
        HttpResponseMessage response = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            FreeTextReason, new[] { "FILES", "DATE_OF_DEATH" });

        // Kiểm tra: Reason là chuỗi mã, không chứa lý do tự do
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<AuditLog> rows = await AuditTestData.ListForVaultAsync(
            factory, submitted.Vault.VaultId, AuditAction.DeathCaseSupplementRequested);
        AuditLog row = Assert.Single(rows);
        Assert.Equal(AuditActorRole.Verifier, row.ActorRole);
        Assert.Equal(submitted.Verifier.User.Id, row.ActorUserId);
        Assert.Equal(submitted.CaseId, row.TargetId);
        Assert.Equal(1, row.TargetVersion);
        Assert.Equal("DATE_OF_DEATH,FILES", row.Reason);
        Assert.DoesNotContain("mờ", row.Reason);
    }

    [SqlFact]
    public async Task SEC07_VerifierFlagReviewed_WritesRow()
    {
        // Chuẩn bị: hồ sơ có một cờ chưa xem xét
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid flagId = await DeathCaseReviewTestData.AddFlagAsync(factory, submitted.CaseId);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.ReviewFlagAsync(
            submitted.Verifier.Client, submitted.CaseId, flagId, detail.GetProperty("etag").GetString(), "Đã xem xét cờ");

        // Kiểm tra: ghi chú tự do không vào nhật ký
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<AuditLog> rows = await AuditTestData.ListForVaultAsync(
            factory, submitted.Vault.VaultId, AuditAction.DeathCaseFlagReviewed);
        AuditLog row = Assert.Single(rows);
        Assert.Equal(AuditActorRole.Verifier, row.ActorRole);
        Assert.Equal(submitted.Verifier.User.Id, row.ActorUserId);
        Assert.Equal(submitted.CaseId, row.TargetId);
        Assert.Null(row.Reason);
    }

    [SqlFact]
    public async Task SEC07_VerifierReject_WritesRow_RepeatDoesNotAdd()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động: từ chối hai lần
        HttpResponseMessage first = await DeathCaseReviewTestData.RejectNowAsync(submitted, FreeTextReason);
        HttpResponseMessage second = await DeathCaseReviewTestData.RejectNowAsync(submitted, FreeTextReason);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        List<AuditLog> rows = await AuditTestData.ListForVaultAsync(
            factory, submitted.Vault.VaultId, AuditAction.DeathCaseRejected);
        AuditLog row = Assert.Single(rows);
        Assert.Equal(AuditActorRole.Verifier, row.ActorRole);
        Assert.Equal(submitted.CaseId, row.TargetId);
        Assert.Null(row.Reason);
    }

    [SqlFact]
    public async Task SEC07_VerifierApprove_WritesRow_RepeatDoesNotAdd()
    {
        // Chuẩn bị: hồ sơ duyệt được
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        // Hành động: duyệt hai lần
        HttpResponseMessage first = await DeathCaseReviewTestData.ApproveNowAsync(submitted, FreeTextReason);
        HttpResponseMessage second = await DeathCaseReviewTestData.ApproveNowAsync(submitted, FreeTextReason);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        List<AuditLog> rows = await AuditTestData.ListForVaultAsync(
            factory, submitted.Vault.VaultId, AuditAction.DeathCaseApproved);
        AuditLog row = Assert.Single(rows);
        Assert.Equal(AuditActorRole.Verifier, row.ActorRole);
        Assert.Equal(submitted.Verifier.User.Id, row.ActorUserId);
        Assert.Equal(submitted.CaseId, row.TargetId);
        Assert.Null(row.Reason);
    }

    [SqlFact]
    public async Task SEC07_ConflictBlockedDecision_WritesNoRow()
    {
        // Chuẩn bị: Verifier được giao trùng email người nhận của kho (ROLE-03)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        Guid verifierId = submitted.Verifier.User.Id;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == verifierId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Email, ApiTestHelpers.BeneficiaryEmail));
        });

        // Hành động
        HttpResponseMessage response = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(),
            FreeTextReason, new[] { "FILES" });

        // Kiểm tra: bị chặn nên không có dòng nhật ký của quyết định
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        List<AuditLog> rows = await AuditTestData.ListForVaultAsync(
            factory, submitted.Vault.VaultId, AuditAction.DeathCaseSupplementRequested);
        Assert.Empty(rows);
    }

    [SqlFact]
    public async Task AC80_SEC13_OwnerSeesDeathCaseRowsInOwnVaultLog()
    {
        // Chuẩn bị: Executor đã gửi hồ sơ
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Guid vaultId = submitted.Vault.VaultId;

        // Hành động: Owner xem nhật ký kho lọc theo loại thao tác
        HttpResponseMessage response = await submitted.Vault.Client.GetAsync(
            $"/api/v1/vaults/{vaultId}/audit-logs?action=DEATH_CASE_SUBMITTED");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        JsonElement item = page.GetProperty("items")[0];
        Assert.Equal("DEATH_CASE_SUBMITTED", item.GetProperty("action").GetString());
        Assert.Equal("EXECUTOR", item.GetProperty("actorRole").GetString());
        Assert.Equal("DEATH_CASE", item.GetProperty("targetType").GetString());
        Assert.Equal(submitted.CaseId, item.GetProperty("targetId").GetGuid());
    }
}
