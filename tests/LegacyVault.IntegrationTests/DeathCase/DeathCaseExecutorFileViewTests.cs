using System.Net;
using System.Text.Json;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Executor xem lại tệp mình đã tải lên hồ sơ chứng tử qua HTTP trên SQL Server thật.</summary>
public sealed class DeathCaseExecutorFileViewTests : IAsyncLifetime
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

    private static string ContentUrl(Guid caseId, Guid fileId)
    {
        return DeathCaseApi.CaseUrl(caseId) + "/files/" + fileId + "/content";
    }

    [SqlFact]
    public async Task ExecutorCanViewTheirOwnUploadedFile_BeforeAndAfterSubmit()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement opened = await DeathCaseApi.OpenAsync(executor.Client, vault.VaultId);
        Guid caseId = opened.GetProperty("id").GetGuid();
        JsonElement uploaded = await DeathCaseApi.UploadPdfAsync(executor.Client, caseId, "giay cua toi", "DEATH_CERTIFICATE");
        Guid fileId = uploaded.GetProperty("currentVersion").GetProperty("files")[0].GetProperty("id").GetGuid();

        // Bản nháp: xem được
        HttpResponseMessage draftView = await executor.Client.GetAsync(ContentUrl(caseId, fileId));
        string draftBody = await draftView.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, draftView.StatusCode);
        Assert.Contains("giay cua toi", draftBody);
        Assert.Equal("application/pdf", draftView.Content.Headers.ContentType!.MediaType);

        // Đã gửi: vẫn xem lại được
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, uploaded);
        Assert.Equal("UNDER_REVIEW", submitted.GetProperty("status").GetString());
        HttpResponseMessage submittedView = await executor.Client.GetAsync(ContentUrl(caseId, fileId));
        Assert.Equal(HttpStatusCode.OK, submittedView.StatusCode);
    }

    [SqlFact]
    public async Task SomeoneWhoIsNotTheExecutorOfTheVaultGetsNotFound()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        DeathCaseTestData.OwnerVault stranger = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        JsonElement opened = await DeathCaseApi.OpenAsync(executor.Client, vault.VaultId);
        Guid caseId = opened.GetProperty("id").GetGuid();
        JsonElement uploaded = await DeathCaseApi.UploadPdfAsync(executor.Client, caseId, "kin", "DEATH_CERTIFICATE");
        Guid fileId = uploaded.GetProperty("currentVersion").GetProperty("files")[0].GetProperty("id").GetGuid();

        HttpResponseMessage asOwner = await vault.Client.GetAsync(ContentUrl(caseId, fileId));
        HttpResponseMessage asStranger = await stranger.Client.GetAsync(ContentUrl(caseId, fileId));
        HttpResponseMessage unknownFile = await executor.Client.GetAsync(ContentUrl(caseId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, asOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, asStranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownFile.StatusCode);
    }
}
