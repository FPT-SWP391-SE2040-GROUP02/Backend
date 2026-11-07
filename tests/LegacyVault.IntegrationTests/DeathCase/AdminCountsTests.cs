using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>API đếm theo trạng thái hoặc vai trò cho các thẻ lọc của Admin: cùng bộ lọc với danh sách nhưng bỏ qua nhóm đang chọn.</summary>
public sealed class AdminCountsTests : IAsyncLifetime
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

    private async Task AddIncidentAsync(string summary, IncidentStatus status, Guid? assignee, IncidentType type = IncidentType.Manual)
    {
        Incident incident = new Incident();
        incident.Type = type;
        incident.Status = status;
        incident.TargetType = "MANUAL";
        incident.TargetId = Guid.NewGuid();
        incident.Summary = summary;
        incident.AssigneeUserId = assignee;
        incident.DueAt = DateTimeOffset.UtcNow.AddDays(1);
        incident.NextReviewAt = DateTimeOffset.UtcNow.AddDays(1);
        incident.CreatedAt = DateTimeOffset.UtcNow;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<Incident>().Add(incident);
            await db.SaveChangesAsync();
        });
    }

    private async Task<HttpClient> AdminAsync(User admin)
    {
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        return await Task.FromResult(client);
    }

    private static async Task<JsonElement> CountsOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("counts");
    }

    [SqlFact]
    public async Task IncidentCounts_GroupByStatus_AndIgnoreStatusFilterButKeepOthers()
    {
        // Chuẩn bị: 2 mở, 1 đang xử lý, 3 đã xử lý; hai sự cố giao cho admin
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        await AddIncidentAsync("sự cố alpha", IncidentStatus.Open, admin.Id);
        await AddIncidentAsync("b", IncidentStatus.Open, null);
        await AddIncidentAsync("c", IncidentStatus.InProgress, admin.Id);
        await AddIncidentAsync("d", IncidentStatus.Resolved, null);
        await AddIncidentAsync("e", IncidentStatus.Resolved, null);
        await AddIncidentAsync("f", IncidentStatus.Resolved, null);
        HttpClient client = await AdminAsync(admin);

        // Hành động
        HttpResponseMessage all = await client.GetAsync("/api/v1/admin/incidents/counts");
        JsonElement allBody = await all.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement mine = await CountsOf(await client.GetAsync("/api/v1/admin/incidents/counts?mine=true"));
        JsonElement ignoresStatus = await CountsOf(await client.GetAsync("/api/v1/admin/incidents/counts?search=alpha"));
        HttpResponseMessage badType = await client.GetAsync("/api/v1/admin/incidents/counts?type=KHONG_CO");

        // Kiểm tra
        Assert.Equal(6, allBody.GetProperty("total").GetInt32());
        JsonElement counts = allBody.GetProperty("counts");
        Assert.Equal(2, counts.GetProperty("OPEN").GetInt32());
        Assert.Equal(1, counts.GetProperty("IN_PROGRESS").GetInt32());
        Assert.Equal(3, counts.GetProperty("RESOLVED").GetInt32());
        Assert.Equal(1, mine.GetProperty("OPEN").GetInt32());
        Assert.Equal(1, mine.GetProperty("IN_PROGRESS").GetInt32());
        Assert.Equal(0, mine.GetProperty("RESOLVED").GetInt32());
        Assert.Equal(1, ignoresStatus.GetProperty("OPEN").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
    }

    [SqlFact]
    public async Task UserCounts_GroupByRole_AndRespectStatusFilter()
    {
        // Chuẩn bị: một Admin đang gọi, hai người dùng thường (một bị khóa), một Verifier
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Locked);
        await DeathCaseTestData.AddUserAsync(factory, UserRole.Verifier, UserStatus.Active);
        HttpClient client = await AdminAsync(admin);

        // Hành động
        HttpResponseMessage all = await client.GetAsync("/api/v1/admin/users/counts");
        JsonElement allBody = await all.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement locked = await CountsOf(await client.GetAsync("/api/v1/admin/users/counts?status=LOCKED"));
        HttpResponseMessage badStatus = await client.GetAsync("/api/v1/admin/users/counts?status=KHONG_CO");

        // Kiểm tra: đủ bốn vai trò, vai trò không có người vẫn là 0
        Assert.Equal(4, allBody.GetProperty("total").GetInt32());
        JsonElement counts = allBody.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("ADMIN").GetInt32());
        Assert.Equal(2, counts.GetProperty("USER").GetInt32());
        Assert.Equal(1, counts.GetProperty("VERIFIER").GetInt32());
        Assert.Equal(0, counts.GetProperty("EXECUTOR").GetInt32());
        Assert.Equal(1, locked.GetProperty("USER").GetInt32());
        Assert.Equal(0, locked.GetProperty("ADMIN").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
    }

    [SqlFact]
    public async Task HoldCounts_AreZeroWhenEmpty_AndNonAdminGets404()
    {
        // Chuẩn bị
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User member = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient adminClient = await AdminAsync(admin);
        HttpClient memberClient = await AdminAsync(member);

        // Hành động
        JsonElement counts = await CountsOf(await adminClient.GetAsync("/api/v1/admin/holds/counts"));
        HttpResponseMessage denied = await memberClient.GetAsync("/api/v1/admin/holds/counts");
        HttpResponseMessage deniedIncidents = await memberClient.GetAsync("/api/v1/admin/incidents/counts");

        // Kiểm tra
        Assert.Equal(0, counts.GetProperty("ACTIVE").GetInt32());
        Assert.Equal(0, counts.GetProperty("RELEASED").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedIncidents.StatusCode);
    }
}
