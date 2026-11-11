using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// API Admin sửa mẫu biên bản: xem mẫu hiện tại (mẫu gốc khi chưa lưu), lưu phiên bản mới, kiểm tra luật, lịch sử, biên bản cũ giữ đúng
/// phiên bản, và chỉ Admin đang hoạt động. Chạy API thật trên SQL Server database tạm.
/// </summary>
public sealed class AdminHandoverTemplatesTests : IAsyncLifetime
{
    private const string BaseUrl = "/api/v1/admin/handover-templates";

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

    private async Task<HttpClient> ClientForRoleAsync(UserRole role)
    {
        User user = await DeathCaseTestData.AddUserAsync(factory, role, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        return client;
    }

    private static object Body(string firstAcceptText, string notice = "Câu báo thử.", string? note = null)
    {
        return new
        {
            accept = new[]
            {
                new { text = firstAcceptText, align = "center", bold = true },
                new { text = "Tôi là {HO_TEN_NGUOI_NHAN}. {CAU_BAO_TU_CHOI}", align = "justify", bold = false },
            },
            decline = new[]
            {
                new { text = "VĂN BẢN TỪ CHỐI THỬ", align = "center", bold = true },
            },
            declineNotice = notice,
            note,
        };
    }

    [SqlFact]
    public async Task Current_WithoutSavedVersion_ReturnsTheBuiltInTemplate()
    {
        HttpClient admin = await ClientForRoleAsync(UserRole.Admin);

        JsonElement body = await admin.GetFromJsonAsync<JsonElement>(BaseUrl + "/current");

        Assert.True(body.GetProperty("isDefault").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("version").ValueKind);
        Assert.True(body.GetProperty("accept").GetArrayLength() > 5);
        Assert.True(body.GetProperty("decline").GetArrayLength() > 5);
        Assert.True(body.GetProperty("tokens").GetArrayLength() >= 10);
        Assert.Equal(
            body.GetProperty("accept").GetArrayLength(), body.GetProperty("default").GetProperty("accept").GetArrayLength());
        Assert.Contains("xóa hoàn toàn khỏi hệ thống", body.GetProperty("declineNotice").GetString());
    }

    [SqlFact]
    public async Task Save_CreatesNewVersion_AndCurrentAndHistoryShowIt()
    {
        HttpClient admin = await ClientForRoleAsync(UserRole.Admin);

        HttpResponseMessage saved = await admin.PostAsJsonAsync(BaseUrl, Body("BIÊN BẢN BẢN 1", "Câu báo 1.", "Luật mới"));
        JsonElement current = await admin.GetFromJsonAsync<JsonElement>(BaseUrl + "/current");
        JsonElement history = await admin.GetFromJsonAsync<JsonElement>(BaseUrl);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.False(current.GetProperty("isDefault").GetBoolean());
        Assert.Equal(1, current.GetProperty("version").GetInt32());
        Assert.Equal("BIÊN BẢN BẢN 1", current.GetProperty("accept")[0].GetProperty("text").GetString());
        Assert.Equal("Câu báo 1.", current.GetProperty("declineNotice").GetString());
        Assert.Equal("Luật mới", current.GetProperty("note").GetString());
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(1, history[0].GetProperty("version").GetInt32());
        Assert.Equal("Luật mới", history[0].GetProperty("note").GetString());
    }

    [SqlFact]
    public async Task Save_Twice_KeepsOldVersionReadableAndResolvable()
    {
        HttpClient admin = await ClientForRoleAsync(UserRole.Admin);
        await admin.PostAsJsonAsync(BaseUrl, Body("BẢN MỘT"));
        await admin.PostAsJsonAsync(BaseUrl, Body("BẢN HAI"));

        JsonElement first = await admin.GetFromJsonAsync<JsonElement>(BaseUrl + "/1");
        JsonElement current = await admin.GetFromJsonAsync<JsonElement>(BaseUrl + "/current");
        HttpResponseMessage missing = await admin.GetAsync(BaseUrl + "/99");

        Assert.Equal("BẢN MỘT", first.GetProperty("accept")[0].GetProperty("text").GetString());
        Assert.Equal(2, current.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        IHandoverTemplateService templates = scope.ServiceProvider.GetRequiredService<IHandoverTemplateService>();
        Assert.Equal("set-2", await templates.ActiveVersionKeyAsync(CancellationToken.None));
        ResolvedHandoverTemplate old = await templates.ResolveAsync("set-1", CancellationToken.None);
        Assert.Equal("set-1", old.VersionKey);
        Assert.Equal("BẢN MỘT", old.Content.Accept[0].Text);
        ResolvedHandoverTemplate unknown = await templates.ResolveAsync("set-99", CancellationToken.None);
        Assert.Equal(HandoverDocumentRules.TemplateVersion, unknown.VersionKey);
        ResolvedHandoverTemplate legacy = await templates.ResolveAsync(HandoverDocumentRules.TemplateVersion, CancellationToken.None);
        Assert.Equal(HandoverTemplateDefaults.Create().Accept.Count, legacy.Content.Accept.Count);
    }

    [SqlFact]
    public async Task Save_InvalidContent_IsRejectedWith400_AndNothingIsSaved()
    {
        HttpClient admin = await ClientForRoleAsync(UserRole.Admin);

        HttpResponseMessage unknownToken = await admin.PostAsJsonAsync(BaseUrl, Body("Có {TRUONG_SAI} ở đây"));
        HttpResponseMessage emptyParagraph = await admin.PostAsJsonAsync(BaseUrl, Body("   "));
        HttpResponseMessage emptyNotice = await admin.PostAsJsonAsync(BaseUrl, Body("Đoạn hợp lệ", "  "));
        HttpResponseMessage longNotice = await admin.PostAsJsonAsync(BaseUrl, Body("Đoạn hợp lệ", new string('a', 501)));
        HttpResponseMessage badAlign = await admin.PostAsJsonAsync(BaseUrl, new
        {
            accept = new[] { new { text = "Đoạn", align = "right", bold = false } },
            decline = new[] { new { text = "Đoạn", align = "left", bold = false } },
            declineNotice = "Câu báo.",
        });
        HttpResponseMessage noParagraphs = await admin.PostAsJsonAsync(BaseUrl, new
        {
            accept = Array.Empty<object>(),
            decline = new[] { new { text = "Đoạn", align = "left", bold = false } },
            declineNotice = "Câu báo.",
        });

        Assert.Equal(HttpStatusCode.BadRequest, unknownToken.StatusCode);
        Assert.Contains("TRUONG_SAI", await unknownToken.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, emptyParagraph.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyNotice.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longNotice.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badAlign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noParagraphs.StatusCode);
        JsonElement history = await admin.GetFromJsonAsync<JsonElement>(BaseUrl);
        Assert.Equal(0, history.GetArrayLength());
    }

    [SqlFact]
    public async Task NonAdmin_GetsNotFoundOnEveryEndpoint()
    {
        HttpClient owner = await ClientForRoleAsync(UserRole.User);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(BaseUrl + "/current")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(BaseUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(BaseUrl + "/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync(BaseUrl, Body("Đoạn"))).StatusCode);
    }
}
