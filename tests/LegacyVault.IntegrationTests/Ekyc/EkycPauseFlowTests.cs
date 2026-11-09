using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Ekyc;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Ekyc;

/// <summary>Công cụ thử nghiệm "tạm ngừng eKYC": chạy trọn luồng eKYC của chủ kho không cần giấy tờ thật.</summary>
public sealed class EkycPauseFlowTests : IAsyncLifetime
{
    private const string PauseUrl = "/api/v1/admin/dev-tools/ekyc-pause";
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
        factory.Services.GetRequiredService<IEkycOptions>().SetPaused(false);
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    [SqlFact]
    public async Task OwnerPassesWholeEkycFlowWhilePausedAndAdminToggleIsAudited()
    {
        // Chuẩn bị: Admin bật tạm ngừng eKYC
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User owner = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient adminClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(adminClient, factory.Services, admin);
        HttpResponseMessage toggled = await adminClient.PutAsJsonAsync(PauseUrl, new { paused = true });
        Assert.Equal(HttpStatusCode.OK, toggled.StatusCode);
        Assert.True((await ApiTestHelpers.ReadJsonAsync(toggled)).GetProperty("paused").GetBoolean());

        // Hành động: chủ kho đi hết các bước eKYC bằng ảnh bất kỳ
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, owner);
        HttpResponseMessage consent = await client.PostAsJsonAsync("/api/v1/ekyc/consent",
            new { purpose = "OWNER_ENROLLMENT", noticeVersion = EkycRules.NoticeVersion });
        Assert.True(consent.IsSuccessStatusCode, await consent.Content.ReadAsStringAsync());

        JsonElement document = await PostImageAsync(client, "/api/v1/ekyc/document");
        Assert.True(document.GetProperty("canMatchFace").GetBoolean());
        HttpResponseMessage confirm = await client.PostAsJsonAsync("/api/v1/ekyc/document/confirm", new
        {
            verificationId = document.GetProperty("verificationId").GetGuid(),
            purpose = "OWNER_ENROLLMENT",
            idNumber = document.GetProperty("idNumber").GetString(),
            fullName = document.GetProperty("fullName").GetString(),
            dateOfBirth = document.GetProperty("dateOfBirth").GetString(),
            residenceAddress = "Địa chỉ thử nghiệm",
        });
        Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync());
        await PostImageAsync(client, "/api/v1/ekyc/document-back");
        Assert.True((await client.PostAsync("/api/v1/ekyc/liveness/start", null)).IsSuccessStatusCode);
        JsonElement frame = await PostImageAsync(client, "/api/v1/ekyc/liveness/frame");
        Assert.True(frame.GetProperty("passed").GetBoolean());
        HttpResponseMessage complete = await client.PostAsync("/api/v1/ekyc/liveness/complete", null);
        Assert.True(complete.IsSuccessStatusCode, await complete.Content.ReadAsStringAsync());

        // Kiểm tra: lượt đạt, danh tính đã gắn, Admin bật được ghi nhật ký
        Assert.Equal("PASSED", (await ApiTestHelpers.ReadJsonAsync(complete)).GetProperty("status").GetString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(EkycStatus.Passed,
                (await db.Set<EkycVerification>().SingleAsync(v => v.UserId == owner.Id)).Status);
            Assert.True(await db.Set<OwnerIdentity>().AnyAsync(i => i.UserId == owner.Id));
            Assert.True(await db.Set<OwnerFaceTemplate>().AnyAsync(t => t.UserId == owner.Id));
            Assert.Equal(1, await db.Set<AuditLog>().CountAsync(l => l.Action == AuditAction.DevToggleEkycPause));
        });
    }

    [SqlFact]
    public async Task NonAdminCannotToggleOrReadPause()
    {
        // Chuẩn bị
        User user = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);

        // Hành động
        HttpResponseMessage put = await client.PutAsJsonAsync(PauseUrl, new { paused = true });
        HttpResponseMessage get = await client.GetAsync(PauseUrl);

        // Kiểm tra: không phải Admin thì bị chặn và eKYC vẫn chạy thật
        Assert.True(put.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.True(get.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.False(factory.Services.GetRequiredService<IEkycOptions>().Paused);
    }

    [SqlFact]
    public async Task PauseIsOffByDefaultAndAdminCanTurnItBackOff()
    {
        // Chuẩn bị
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);

        // Hành động và kiểm tra: mặc định tắt, bật rồi tắt lại về đúng giá trị
        Assert.False((await client.GetFromJsonAsync<JsonElement>(PauseUrl)).GetProperty("paused").GetBoolean());
        await client.PutAsJsonAsync(PauseUrl, new { paused = true });
        Assert.True((await client.GetFromJsonAsync<JsonElement>(PauseUrl)).GetProperty("paused").GetBoolean());
        await client.PutAsJsonAsync(PauseUrl, new { paused = false });
        Assert.False((await client.GetFromJsonAsync<JsonElement>(PauseUrl)).GetProperty("paused").GetBoolean());
    }

    private static async Task<JsonElement> PostImageAsync(HttpClient client, string url)
    {
        using MultipartFormDataContent form = new MultipartFormDataContent();
        ByteArrayContent file = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9 });
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "image.jpg");
        HttpResponseMessage response = await client.PostAsync(url, form);
        Assert.True(response.IsSuccessStatusCode, url + ": " + await response.Content.ReadAsStringAsync());
        return await ApiTestHelpers.ReadJsonAsync(response);
    }
}
