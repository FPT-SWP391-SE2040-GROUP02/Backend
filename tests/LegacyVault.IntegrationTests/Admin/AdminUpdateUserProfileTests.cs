using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Admin;

/// <summary>Admin sửa họ tên, số điện thoại, số chứng chỉ và số thẻ luật sư của tài khoản nhân sự. Chạy trên SQL Server thật ([SqlFact]).</summary>
public sealed class AdminUpdateUserProfileTests : IAsyncLifetime
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

    private HttpClient ClientFor(User user)
    {
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        return client;
    }

    private static string ProfileUrl(Guid userId)
    {
        return $"/api/v1/admin/users/{userId}/profile";
    }

    [SqlFact]
    public async Task Admin_UpdatesVerifierProfile_ValuesAreNormalizedShownInDetailAndAudited()
    {
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User verifier = await VaultTestData.AddVerifierAsync(factory);
        HttpClient client = ClientFor(admin);

        HttpResponseMessage response = await client.PutAsJsonAsync(ProfileUrl(verifier.Id), new
        {
            fullName = "  Lê Thu Hà  ",
            phone = "091 234-56.78",
            practiceCertificateNumber = " CC-12345 ",
            lawyerCardNumber = "LS-678"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Lê Thu Hà", (await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("fullName").GetString());
        JsonElement detail = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync($"/api/v1/admin/users/{verifier.Id}"));
        Assert.Equal("0912345678", detail.GetProperty("phone").GetString());
        Assert.Equal("CC-12345", detail.GetProperty("practiceCertificateNumber").GetString());
        Assert.Equal("LS-678", detail.GetProperty("lawyerCardNumber").GetString());
        int audited = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            audited = await db.Set<AuditLog>().CountAsync(l => l.Action == AuditAction.AdminUpdatedUserProfile && l.TargetId == verifier.Id);
        });
        Assert.Equal(1, audited);
    }

    [SqlFact]
    public async Task Admin_ClearsOptionalFields_WhenLeftEmpty()
    {
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User verifier = await VaultTestData.AddVerifierAsync(factory);
        HttpClient client = ClientFor(admin);
        await client.PutAsJsonAsync(ProfileUrl(verifier.Id), new
        {
            fullName = "Lê Thu Hà", phone = "0912345678", practiceCertificateNumber = "CC-1", lawyerCardNumber = "LS-1"
        });

        HttpResponseMessage cleared = await client.PutAsJsonAsync(ProfileUrl(verifier.Id), new { fullName = "Lê Thu Hà", phone = "" });

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        JsonElement detail = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync($"/api/v1/admin/users/{verifier.Id}"));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("phone").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("practiceCertificateNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("lawyerCardNumber").ValueKind);
    }

    [SqlFact]
    public async Task Admin_Update_RejectsBlankNameWrongPhoneAndPhoneOfAnotherAccount()
    {
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User verifier = await VaultTestData.AddVerifierAsync(factory);
        User other = await VaultTestData.AddVerifierAsync(factory);
        HttpClient client = ClientFor(admin);
        await client.PutAsJsonAsync(ProfileUrl(other.Id), new { fullName = "Người Khác", phone = "0987654321" });

        await ApiTestHelpers.AssertProblemAsync(
            await client.PutAsJsonAsync(ProfileUrl(verifier.Id), new { fullName = "   " }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await client.PutAsJsonAsync(ProfileUrl(verifier.Id), new { fullName = "Lê Thu Hà", phone = "3232" }),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await client.PutAsJsonAsync(ProfileUrl(verifier.Id), new { fullName = "Lê Thu Hà", phone = "098 765 4321" }),
            HttpStatusCode.Conflict, "PHONE_ALREADY_EXISTS");
        // Giữ nguyên số của chính mình thì không bị coi là trùng.
        await client.PutAsJsonAsync(ProfileUrl(verifier.Id), new { fullName = "Lê Thu Hà", phone = "0912345678" });
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            ProfileUrl(verifier.Id), new { fullName = "Lê Thu Hà Mới", phone = "0912 345 678" })).StatusCode);
    }

    [SqlFact]
    public async Task Admin_Update_OnlyForStaffAccounts_AndOnlyByAdmin()
    {
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User owner = await VaultTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        User verifier = await VaultTestData.AddVerifierAsync(factory);
        HttpClient adminClient = ClientFor(admin);

        // Chủ tài sản (người dùng thường) không thuộc phạm vi sửa của Admin.
        HttpResponseMessage forOwner = await adminClient.PutAsJsonAsync(ProfileUrl(owner.Id), new { fullName = "Tên Mới" });
        await ApiTestHelpers.AssertProblemAsync(forOwner, HttpStatusCode.Conflict, "ADMIN_ACTION_NOT_ALLOWED");
        // Tài khoản không tồn tại và người không phải Admin: 404.
        await ApiTestHelpers.AssertProblemAsync(
            await adminClient.PutAsJsonAsync(ProfileUrl(Guid.NewGuid()), new { fullName = "X" }), HttpStatusCode.NotFound, "USER_NOT_FOUND");
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(verifier).PutAsJsonAsync(ProfileUrl(verifier.Id), new { fullName = "Tự Sửa" })).StatusCode);
    }

    [SqlFact]
    public async Task Admin_Update_AdminAccountIgnoresCertificateNumbers()
    {
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = ClientFor(admin);

        HttpResponseMessage response = await client.PutAsJsonAsync(ProfileUrl(admin.Id), new
        {
            fullName = "Quản Trị Mới", practiceCertificateNumber = "CC-9", lawyerCardNumber = "LS-9"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement detail = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync($"/api/v1/admin/users/{admin.Id}"));
        Assert.Equal("Quản Trị Mới", detail.GetProperty("fullName").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("practiceCertificateNumber").ValueKind);
    }
}
