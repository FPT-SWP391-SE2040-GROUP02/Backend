using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Các khối mới của dashboard Admin: kế hoạch theo giai đoạn, tải việc người xét duyệt, hoạt động gần đây.</summary>
public sealed class AdminDashboardWidgetsTests : IAsyncLifetime
{
    private const string DashboardUrl = "/api/v1/admin/dashboard";

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
    public async Task EmptySystem_ReturnsEmptyWidgets()
    {
        // Chuẩn bị
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);

        // Hành động
        HttpResponseMessage response = await client.GetAsync(DashboardUrl);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("planStages").GetArrayLength());
        Assert.Equal(0, body.GetProperty("closedPlans").GetInt32());
        Assert.Equal(0, body.GetProperty("verifierLoad").GetArrayLength());
        Assert.True(body.GetProperty("recentActivity").GetArrayLength() <= 5);
    }

    [SqlFact]
    public async Task CaseUnderReview_ShowsPlanStageAndVerifierLoad()
    {
        // Chuẩn bị: một hồ sơ đang xét có Verifier được giao, và một Admin
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);

        // Hành động
        JsonElement body = await client.GetFromJsonAsync<JsonElement>(DashboardUrl);

        // Kiểm tra: một kế hoạch ở giai đoạn có hồ sơ chứng tử, Verifier đang giữ 1 hồ sơ chưa quá hạn
        int planCount = 0;
        foreach (JsonElement stage in body.GetProperty("planStages").EnumerateArray())
        {
            planCount += stage.GetProperty("count").GetInt32();
        }
        Assert.True(planCount >= 1);
        JsonElement load = body.GetProperty("verifierLoad");
        Assert.Equal(1, load.GetArrayLength());
        Assert.Equal(submitted.Verifier.User.Id, load[0].GetProperty("userId").GetGuid());
        Assert.Equal(1, load[0].GetProperty("openCases").GetInt32());
        Assert.Equal(0, load[0].GetProperty("overdueCases").GetInt32());
    }

    [SqlFact]
    public async Task NonAdmin_Gets404()
    {
        // Chuẩn bị
        User user = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);

        // Hành động và kiểm tra
        HttpResponseMessage response = await client.GetAsync(DashboardUrl);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
