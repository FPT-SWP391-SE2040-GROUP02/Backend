using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Bộ lọc "Của tôi" và tìm kiếm của danh sách sự cố Admin, chạy API thật trên SQL Server database tạm.</summary>
public sealed class AdminIncidentListFilterTests : IAsyncLifetime
{
    private const string ListUrl = "/api/v1/admin/incidents";

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

    private async Task AddIncidentAsync(string summary, Guid? assignee)
    {
        Incident incident = new Incident();
        incident.Type = IncidentType.Manual;
        incident.Status = IncidentStatus.Open;
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

    private static async Task<List<string>> SummariesAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        List<string> summaries = new List<string>();
        foreach (JsonElement item in page.GetProperty("items").EnumerateArray())
        {
            summaries.Add(item.GetProperty("summary").GetString()!);
        }
        return summaries;
    }

    [SqlFact]
    public async Task Mine_ReturnsOnlyIncidentsAssignedToTheCurrentAdmin()
    {
        // Chuẩn bị: hai Admin, mỗi người một sự cố, thêm một sự cố chưa giao
        User me = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User other = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        await AddIncidentAsync("của tôi", me.Id);
        await AddIncidentAsync("của người khác", other.Id);
        await AddIncidentAsync("chưa giao", null);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, me);

        // Hành động
        List<string> mine = await SummariesAsync(await client.GetAsync(ListUrl + "?mine=true"));
        List<string> all = await SummariesAsync(await client.GetAsync(ListUrl));

        // Kiểm tra
        Assert.Equal(new List<string> { "của tôi" }, mine);
        Assert.Equal(3, all.Count);
    }

    [SqlFact]
    public async Task Mine_IgnoresAssigneeFromClient_AndSearchNarrowsResult()
    {
        // Chuẩn bị
        User me = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User other = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        await AddIncidentAsync("kho A lỗi", me.Id);
        await AddIncidentAsync("kho B lỗi", me.Id);
        await AddIncidentAsync("kho C lỗi", other.Id);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, me);

        // Hành động: truyền assigneeUserId của người khác cùng mine=true, rồi thêm từ khóa
        List<string> spoofed = await SummariesAsync(
            await client.GetAsync(ListUrl + "?mine=true&assigneeUserId=" + other.Id));
        List<string> searched = await SummariesAsync(await client.GetAsync(ListUrl + "?mine=true&search=kho%20B"));

        // Kiểm tra
        Assert.Equal(2, spoofed.Count);
        Assert.DoesNotContain("kho C lỗi", spoofed);
        Assert.Equal(new List<string> { "kho B lỗi" }, searched);
    }

    [SqlFact]
    public async Task List_ReturnsAssigneeNameAndRoleOrNullWhenUnassigned()
    {
        // Chuẩn bị: một sự cố giao cho Người xét duyệt có tên, một sự cố chưa giao
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User verifier = await DeathCaseTestData.AddUserAsync(factory, UserRole.Verifier, UserStatus.Active);
        await AddIncidentAsync("giao cho người xét duyệt", verifier.Id);
        await AddIncidentAsync("chưa giao", null);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);

        // Hành động
        JsonElement page = await client.GetFromJsonAsync<JsonElement>(ListUrl);

        // Kiểm tra: có họ tên và vai trò để hiện thẳng trên danh sách, chưa giao thì null
        foreach (JsonElement item in page.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("summary").GetString() == "giao cho người xét duyệt")
            {
                Assert.Equal(verifier.FullName, item.GetProperty("assigneeFullName").GetString());
                Assert.Equal("VERIFIER", item.GetProperty("assigneeRole").GetString());
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, item.GetProperty("assigneeFullName").ValueKind);
                Assert.Equal(JsonValueKind.Null, item.GetProperty("assigneeRole").ValueKind);
            }
        }
    }
}
