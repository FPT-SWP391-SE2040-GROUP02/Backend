using System.Net;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Executor được báo bằng thư khi Verifier yêu cầu bổ sung hoặc từ chối, và vẫn mở lại được hồ sơ bị từ chối
/// gần nhất từ danh sách kho.
/// </summary>
public sealed class ExecutorDecisionNoticeTests : IAsyncLifetime
{
    private readonly FakeDeathCaseExecutorNoticeSender sender = new FakeDeathCaseExecutorNoticeSender();

    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IDeathCaseExecutorNoticeSender>();
            services.AddSingleton<IDeathCaseExecutorNoticeSender>(sender);
        };
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
    public async Task SupplementRequest_EmailsExecutorOnce()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        string? etag = detail.GetProperty("etag").GetString();

        // Hành động: gọi hai lần cùng nội dung (lần hai là gọi lặp)
        HttpResponseMessage first = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, etag, "Ảnh mờ", new[] { "FILES" });
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(first);
        HttpResponseMessage repeat = await DeathCaseReviewTestData.RequestSupplementAsync(
            submitted.Verifier.Client, submitted.CaseId, body.GetProperty("etag").GetString(), "Ảnh mờ", new[] { "FILES" });

        // Kiểm tra: một thư cho Executor, có hạn bổ sung, không có lý do Verifier ghi
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        DeathCaseExecutorNotice notice = Assert.Single(sender.Sent);
        Assert.Equal(submitted.CaseId, notice.CaseId);
        Assert.Equal(submitted.Executor.User.Email, notice.ToEmail);
        Assert.Equal(DeathCaseDecisionKind.SupplementRequested, notice.Kind);
        Assert.NotNull(notice.SupplementDueAt);
        Assert.Equal(8, notice.CaseCode.Length);
    }

    [SqlFact]
    public async Task Reject_EmailsExecutorAndVaultListKeepsRejectedCase()
    {
        // Chuẩn bị
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        HttpResponseMessage rejected = await DeathCaseReviewTestData.RejectNowAsync(submitted, "Giấy không hợp lệ.");
        HttpResponseMessage repeat = await DeathCaseReviewTestData.RejectNowAsync(submitted, "Giấy không hợp lệ.");
        HttpResponseMessage vaults = await submitted.Executor.Client.GetAsync("/api/v1/executor/vaults");
        JsonElement list = await ApiTestHelpers.ReadJsonAsync(vaults);

        // Kiểm tra: một thư Rejected không có hạn; danh sách kho không còn hồ sơ đang mở nhưng chỉ ra hồ sơ bị từ chối
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        DeathCaseExecutorNotice notice = Assert.Single(sender.Sent);
        Assert.Equal(DeathCaseDecisionKind.Rejected, notice.Kind);
        Assert.Null(notice.SupplementDueAt);

        Assert.Equal(HttpStatusCode.OK, vaults.StatusCode);
        JsonElement vault = list[0];
        Assert.Equal(JsonValueKind.Null, vault.GetProperty("openDeathCaseId").ValueKind);
        Assert.Equal(submitted.CaseId, vault.GetProperty("lastRejectedDeathCaseId").GetGuid());

        HttpResponseMessage reopened = await submitted.Executor.Client.GetAsync(DeathCaseApi.CaseUrl(submitted.CaseId));
        JsonElement caseBody = await ApiTestHelpers.ReadJsonAsync(reopened);
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal("REJECTED", caseBody.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task VaultList_NoRejectedCase_ReturnsNull()
    {
        // Chuẩn bị: hồ sơ đang xét, chưa từng bị từ chối
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        HttpResponseMessage vaults = await submitted.Executor.Client.GetAsync("/api/v1/executor/vaults");
        JsonElement list = await ApiTestHelpers.ReadJsonAsync(vaults);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, vaults.StatusCode);
        Assert.Equal(submitted.CaseId, list[0].GetProperty("openDeathCaseId").GetGuid());
        Assert.Equal(JsonValueKind.Null, list[0].GetProperty("lastRejectedDeathCaseId").ValueKind);
        Assert.Empty(sender.Sent);
    }
}
