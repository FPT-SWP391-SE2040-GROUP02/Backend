using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>Công cụ thử nghiệm: Admin đặt lại eKYC của một tài khoản để làm lại từ đầu (chỉ có ở Development).</summary>
public sealed class DevToolsEkycResetTests : IAsyncLifetime
{
    private const string Url = "/api/v1/admin/dev-tools/users/ekyc-reset";
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
    public async Task AdminRemovesAllEkycDataOfTargetAndKeepsAccount()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User target = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        User other = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await AddEkycDataAsync(target.Id);
        await AddEkycDataAsync(other.Id);

        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        HttpResponseMessage response = await client.PostAsJsonAsync(Url, new { email = " " + target.Email.ToUpperInvariant() + " " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(target.Id, body.GetProperty("userId").GetGuid());
        Assert.Equal(1, body.GetProperty("verificationsRemoved").GetInt32());
        Assert.Equal(1, body.GetProperty("consentsRemoved").GetInt32());
        Assert.True(body.GetProperty("identityRemoved").GetBoolean());
        Assert.Equal(1, body.GetProperty("faceTemplatesRemoved").GetInt32());

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<EkycVerification>().CountAsync(v => v.UserId == target.Id));
            Assert.Equal(0, await db.Set<EkycConsent>().CountAsync(c => c.UserId == target.Id));
            Assert.Equal(0, await db.Set<OwnerIdentity>().CountAsync(i => i.UserId == target.Id));
            Assert.Equal(0, await db.Set<OwnerFaceTemplate>().CountAsync(t => t.UserId == target.Id));
            Assert.True(await db.Set<User>().AnyAsync(u => u.Id == target.Id));
            // Tài khoản khác không bị đụng tới.
            Assert.Equal(1, await db.Set<EkycVerification>().CountAsync(v => v.UserId == other.Id));
            Assert.Equal(1, await db.Set<OwnerIdentity>().CountAsync(i => i.UserId == other.Id));
            AuditLog log = await db.Set<AuditLog>().SingleAsync(l => l.Action == AuditAction.DevEkycReset);
            Assert.Equal(admin.Id, log.ActorUserId);
            Assert.Equal(target.Id, log.TargetId);
            Assert.Null(log.VaultId);
        });
    }

    [SqlFact]
    public async Task AccountWithoutEkycReturnsZerosAndWritesNoAudit()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User target = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);

        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        HttpResponseMessage response = await client.PostAsJsonAsync(Url, new { email = target.Email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(0, body.GetProperty("verificationsRemoved").GetInt32());
        Assert.False(body.GetProperty("identityRemoved").GetBoolean());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.False(await db.Set<AuditLog>().AnyAsync(l => l.Action == AuditAction.DevEkycReset));
        });
    }

    [SqlFact]
    public async Task UnknownEmailIs404_NonAdminIs404()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User plain = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await AddEkycDataAsync(plain.Id);

        HttpClient adminClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(adminClient, factory.Services, admin);
        HttpResponseMessage unknown = await adminClient.PostAsJsonAsync(Url, new { email = "khong-co@test.local" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        HttpClient userClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(userClient, factory.Services, plain);
        HttpResponseMessage forbidden = await userClient.PostAsJsonAsync(Url, new { email = plain.Email });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<EkycVerification>().CountAsync(v => v.UserId == plain.Id));
        });
    }

    /// <summary>Một lượt eKYC đã đạt, một lần đồng ý, danh tính và mẫu khuôn mặt giả (dữ liệu mã hóa giả, không có định danh thật).</summary>
    private Task AddEkycDataAsync(Guid userId)
    {
        return ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            EkycVerification verification = new EkycVerification();
            verification.UserId = userId;
            verification.Purpose = EkycPurpose.OwnerEnrollment;
            verification.Status = EkycStatus.Passed;
            verification.IdentityMatch = EkycIdentityMatch.NotChecked;
            verification.CitizenIdLast4 = "1234";
            verification.CreatedAt = now;
            verification.CompletedAt = now;
            db.Add(verification);

            EkycConsent consent = new EkycConsent();
            consent.UserId = userId;
            consent.Purpose = EkycPurpose.OwnerEnrollment;
            consent.NoticeVersion = "2026-10-v1";
            consent.ConsentedAt = now;
            db.Add(consent);

            OwnerIdentity identity = new OwnerIdentity();
            identity.UserId = userId;
            identity.CitizenIdLast4 = "1234";
            identity.SourceVerificationId = verification.Id;
            identity.EncryptionKeyId = "test";
            identity.WrappedDataKey = new byte[] { 1 };
            identity.IdentityEncrypted = new byte[] { 2 };
            identity.CreatedAt = now;
            db.Add(identity);

            OwnerFaceTemplate template = new OwnerFaceTemplate();
            template.UserId = userId;
            template.Version = 1;
            template.ModelName = "test";
            template.Dimension = 128;
            template.SourceVerificationId = verification.Id;
            template.EncryptionKeyId = "test";
            template.WrappedDataKey = new byte[] { 1 };
            template.EmbeddingEncrypted = new byte[] { 2 };
            template.CreatedAt = now;
            db.Add(template);

            await db.SaveChangesAsync();
        });
    }
}
