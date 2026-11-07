using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>API chi tiết một tài khoản cho Admin: thông tin, hồ sơ đang giữ, hoạt động gần đây, quyền truy cập.</summary>
public sealed class AdminUserDetailTests : IAsyncLifetime
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

    private async Task<HttpClient> AdminClientAsync()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        return client;
    }

    [SqlFact]
    public async Task Verifier_ShowsAssignedCaseAndNoSecrets()
    {
        // Chuẩn bị: một hồ sơ đang xét giao cho một Verifier
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpClient admin = await AdminClientAsync();

        // Hành động
        HttpResponseMessage response = await admin.GetAsync("/api/v1/admin/users/" + submitted.Verifier.User.Id);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync();
        JsonElement body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(submitted.Verifier.User.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("VERIFIER", body.GetProperty("role").GetString());
        JsonElement cases = body.GetProperty("assignedCases");
        Assert.Equal(1, cases.GetArrayLength());
        Assert.Equal(submitted.CaseId, cases[0].GetProperty("caseId").GetGuid());
        Assert.False(cases[0].GetProperty("overdue").GetBoolean());
        Assert.Equal(0, body.GetProperty("recentDecisions").GetArrayLength());
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task NormalUser_HasNoCasesAndUnknownIdIs404()
    {
        // Chuẩn bị
        User member = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient admin = await AdminClientAsync();

        // Hành động
        JsonElement body = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/users/" + member.Id);
        HttpResponseMessage missing = await admin.GetAsync("/api/v1/admin/users/" + Guid.NewGuid());

        // Kiểm tra
        Assert.Equal("USER", body.GetProperty("role").GetString());
        Assert.Equal(0, body.GetProperty("assignedCases").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [SqlFact]
    public async Task NonAdmin_Gets404()
    {
        // Chuẩn bị
        User member = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        User other = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, member);

        // Hành động
        HttpResponseMessage response = await client.GetAsync("/api/v1/admin/users/" + other.Id);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
