using System.Net;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Executor gửi hồ sơ: AI phải nhận ra ít nhất một trang là giấy chứng tử; AI lỗi thì vẫn gửi được nhưng có cờ.
/// </summary>
public sealed class DeathCertificateCheckFlowTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private readonly FakeDeathCertificateClassifier classifier = new FakeDeathCertificateClassifier();

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
            services.RemoveAll<IDeathCertificateClassifier>();
            services.AddSingleton<IDeathCertificateClassifier>(classifier);
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
    public async Task NoPageIsDeathCertificate_SubmitBlockedAndPlanNotLocked()
    {
        // Chuẩn bị: AI xem được mọi trang nhưng không trang nào là giấy chứng tử
        classifier.Status = DeathCertificateCheckStatus.NotRecognized;
        classifier.DocumentType = "Căn cước công dân";
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        Guid caseId = prepared.GetProperty("id").GetGuid();
        await DeathCaseApi.UploadPdfAsync(executor.Client, caseId, "trang thu hai");
        JsonElement latest = await ReadCaseAsync(executor.Client, caseId);

        // Hành động
        HttpResponseMessage response = await DeathCaseApi.SubmitAsync(
            executor.Client, caseId, latest.GetProperty("etag").GetString());

        // Kiểm tra: 409 kèm mã mới và kết quả từng trang; hồ sơ vẫn là bản nháp, kế hoạch chưa khóa
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_INCOMPLETE");
        Assert.Equal(
            new List<string> { DeathCertificateCheckRules.NotRecognizedCode },
            DeathCaseApi.ReadStrings(problem.GetProperty("missingRequirements")));
        JsonElement fileChecks = problem.GetProperty("fileChecks");
        Assert.Equal(2, fileChecks.GetArrayLength());
        Assert.Equal("NOT_RECOGNIZED", fileChecks[0].GetProperty("status").GetString());
        Assert.Equal("Căn cước công dân", fileChecks[0].GetProperty("documentType").GetString());
        Assert.Equal(2, classifier.CallCount);
        Assert.Equal(PlanStatus.Active, await LoadPlanStatusAsync(vault.VaultId));
        DeathCaseVersion version = await LoadVersionAsync(caseId);
        Assert.Null(version.SubmittedAt);
    }

    [SqlFact]
    public async Task AiUnavailable_SubmitSucceedsWithUnverifiedFlag()
    {
        // Chuẩn bị: AI không kiểm tra được (chưa có key, lỗi mạng...)
        classifier.Status = DeathCertificateCheckStatus.Unavailable;
        classifier.DocumentType = null;
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);

        // Kiểm tra: gửi được, có cờ chặn duyệt tới khi Verifier xem xét
        Assert.Equal("UNDER_REVIEW", submitted.GetProperty("status").GetString());
        List<DeathCaseFlag> flags = await LoadFlagsAsync(submitted.GetProperty("id").GetGuid());
        DeathCaseFlag flag = Assert.Single(flags);
        Assert.Equal(DeathCaseFlagKind.CertificateUnverified, flag.Kind);
        Assert.Null(flag.ReviewedAt);
    }

    [SqlFact]
    public async Task RecognizedPage_SubmitSucceedsWithoutFlagAndStopsEarly()
    {
        // Chuẩn bị: hai trang, AI nhận ra ngay trang đầu
        classifier.Status = DeathCertificateCheckStatus.Recognized;
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        Guid caseId = prepared.GetProperty("id").GetGuid();
        JsonElement latest = await DeathCaseApi.UploadPdfAsync(executor.Client, caseId, "trang thu hai");

        // Hành động
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, latest);

        // Kiểm tra
        Assert.Equal("UNDER_REVIEW", submitted.GetProperty("status").GetString());
        Assert.Empty(await LoadFlagsAsync(caseId));
        Assert.Equal(1, classifier.CallCount);
    }

    private static async Task<JsonElement> ReadCaseAsync(HttpClient client, Guid caseId)
    {
        HttpResponseMessage response = await client.GetAsync(DeathCaseApi.CaseUrl(caseId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    private async Task<PlanStatus> LoadPlanStatusAsync(Guid vaultId)
    {
        PlanStatus status = PlanStatus.Closed;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            status = await db.HandoverPlans.Where(p => p.VaultId == vaultId).Select(p => p.Status).SingleAsync();
        });
        return status;
    }

    private async Task<DeathCaseVersion> LoadVersionAsync(Guid caseId)
    {
        DeathCaseVersion? result = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<DeathCaseVersion>().AsNoTracking()
                .SingleAsync(v => v.CaseId == caseId && v.VersionNo == 1);
        });
        return result!;
    }

    private async Task<List<DeathCaseFlag>> LoadFlagsAsync(Guid caseId)
    {
        List<DeathCaseFlag> result = new List<DeathCaseFlag>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            result = await db.Set<DeathCaseFlag>().AsNoTracking().Where(f => f.CaseId == caseId).ToListAsync();
        });
        return result;
    }
}
