using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Đường HTTP của hồ sơ người nhận: định tuyến, gắn form nhiều phần, phân quyền và lỗi chuẩn (chạy trên SQL Server tạm).</summary>
public sealed class HandoverSubmissionApiTests : IAsyncLifetime
{
    private static readonly byte[] Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
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

    private static MultipartFormDataContent Form()
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new StringContent("ACCEPT"), "Kind");
        form.Add(new StringContent("Trần Minh Anh"), "FullName");
        form.Add(new StringContent("012345671234"), "CitizenId");
        form.Add(new StringContent("1990-01-01"), "DateOfBirth");
        form.Add(new StringContent("true"), "Acknowledged");
        form.Add(new StringContent("false"), "VoluntaryDecline");
        foreach (string name in new[] { "FrontImage", "BackImage" })
        {
            ByteArrayContent image = new ByteArrayContent(Png);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(image, name, name + ".png");
        }
        return form;
    }

    [SqlFact]
    public async Task Endpoints_RequireLogin()
    {
        HttpClient anonymous = factory.CreateClient();

        HttpResponseMessage list = await anonymous.GetAsync("/api/v1/verifier/handover-submissions");
        HttpResponseMessage submit = await anonymous.PostAsync(
            "/api/v1/handover/turns/" + Guid.NewGuid() + "/submission", Form());

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, submit.StatusCode);
    }

    [SqlFact]
    public async Task Verifier_ListsNothingWhenHeApprovedNoCase_AndAnOrdinaryUserIsForbidden()
    {
        User verifier = await DeathCaseTestData.AddUserAsync(factory, UserRole.Verifier, UserStatus.Active);
        User user = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient verifierClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(verifierClient, factory.Services, verifier);
        HttpClient userClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(userClient, factory.Services, user);

        HttpResponseMessage ok = await verifierClient.GetAsync("/api/v1/verifier/handover-submissions");
        HttpResponseMessage denied = await userClient.GetAsync("/api/v1/verifier/handover-submissions");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(0, (await ApiTestHelpers.ReadJsonAsync(ok)).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [SqlFact]
    public async Task Submit_ForUnknownTurn_IsNotFoundAndNothingIsLeaked()
    {
        User user = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);

        HttpResponseMessage response = await client.PostAsync(
            "/api/v1/handover/turns/" + Guid.NewGuid() + "/submission", Form());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        JsonElement problem = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("HANDOVER_NOT_FOUND", problem.GetProperty("code").GetString());
    }

    [SqlFact]
    public async Task Submit_WithMissingImages_IsValidationErrorBeforeAnyLookup()
    {
        User user = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        using MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new StringContent("ACCEPT"), "Kind");

        HttpResponseMessage response = await client.PostAsync(
            "/api/v1/handover/turns/" + Guid.NewGuid() + "/submission", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SqlFact]
    public async Task GetLatest_ForUnknownTurn_IsNotFound()
    {
        User user = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);

        HttpResponseMessage response = await client.GetAsync("/api/v1/handover/turns/" + Guid.NewGuid() + "/submission");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
