using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.DeathCases;
using LegacyVault.Infrastructure.Handover;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// LV-01: tính đúng đắn của dữ liệu khi nhiều request chạy cùng lúc (giữ chỗ dung lượng, CCCD trùng)
/// và việc không coi "chưa cấu hình email" là đã giao thông báo.
/// </summary>
public sealed class DataIntegrityTests : IAsyncLifetime
{
    private const long MiB = 1_048_576L;

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

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid vaultId, int megabytes)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[megabytes * MiB]), "file", "tep.bin");
        return client.PostAsync($"/api/v1/vaults/{vaultId}/assets", form);
    }

    [SqlFact]
    public async Task Quota02_ConcurrentUploads_NeverExceedQuota()
    {
        // Kho Free 20 MiB, sáu lần tải 5 MiB cùng lúc: tối đa bốn lần được nhận
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);

        List<Task<HttpResponseMessage>> uploads = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 6; i++)
        {
            uploads.Add(UploadAsync(client, vaultId, 5));
        }
        HttpResponseMessage[] responses = await Task.WhenAll(uploads);

        int created = 0;
        foreach (HttpResponseMessage response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Created)
            {
                created++;
            }
            else
            {
                await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "STORAGE_QUOTA_EXCEEDED");
            }
        }
        Assert.Equal(4, created);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            long used = await db.AssetVersions.Where(v => v.Asset.VaultId == vaultId).SumAsync(v => v.SizeBytes);
            Assert.True(used <= 20 * MiB);
            Assert.Equal(0, await db.Set<StorageReservation>().CountAsync(r => r.Status == StorageReservationStatus.Active
                && r.VaultId == vaultId));
        });
    }

    [SqlFact]
    public async Task Quota02_ActiveReservationCounts_ExpiredOneDoesNot()
    {
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);

        // Một lần tải khác đang giữ 18 MiB còn hạn: chỉ còn 2 MiB
        Guid reservationId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            StorageReservation held = new StorageReservation();
            held.VaultId = vaultId;
            held.Bytes = 18 * MiB;
            held.Status = StorageReservationStatus.Active;
            held.CreatedAt = DateTimeOffset.UtcNow;
            held.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            db.Set<StorageReservation>().Add(held);
            await db.SaveChangesAsync();
            reservationId = held.Id;
        });
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            await UploadAsync(client, vaultId, 5), HttpStatusCode.Conflict, "STORAGE_QUOTA_EXCEEDED");
        Assert.Equal(2 * MiB, problem.GetProperty("storageRemainingBytes").GetInt64());

        // Hết hạn thì tự giải phóng
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<StorageReservation>().Where(r => r.Id == reservationId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        });
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(client, vaultId, 5)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<StorageReservation>().CountAsync(r => r.VaultId == vaultId && r.Status == StorageReservationStatus.Committed));
        });
    }

    [SqlFact]
    public async Task Quota04_SameCitizenIdAtOnce_OneProfile()
    {
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        string url = $"/api/v1/vaults/{vaultId}/beneficiaries";
        object body = new { citizenId = "012345678901", fullName = "Nguyễn Văn Nhận", dateOfBirth = "1990-05-20", email = "nhan@example.com" };

        List<Task<HttpResponseMessage>> posts = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 6; i++)
        {
            posts.Add(client.PostAsJsonAsync(url, body));
        }
        HttpResponseMessage[] responses = await Task.WhenAll(posts);

        int created = 0;
        HashSet<Guid> ids = new HashSet<Guid>();
        foreach (HttpResponseMessage response in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
            if (response.StatusCode == HttpStatusCode.Created)
            {
                created++;
            }
            ids.Add((await ApiTestHelpers.ReadJsonAsync(response)).GetProperty("id").GetGuid());
        }
        Assert.Equal(1, created);
        Assert.Single(ids);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Beneficiary stored = await db.Beneficiaries.SingleAsync(b => b.VaultId == vaultId);
            Assert.NotNull(stored.CitizenIdHash);
            Assert.Equal(32, stored.CitizenIdHash!.Length);
        });
    }

    [SqlFact]
    public async Task Quota04_SameCitizenIdDifferentName_StillConflict_DifferentVaultAllowed()
    {
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        (HttpClient otherClient, Guid otherVaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "012345678901", "Nguyễn Văn Nhận");

        HttpResponseMessage conflict = await client.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/beneficiaries",
            new { citizenId = "012345678901", fullName = "Người Khác", dateOfBirth = "1990-05-20", email = "a@example.com" });
        HttpResponseMessage otherVault = await otherClient.PostAsJsonAsync($"/api/v1/vaults/{otherVaultId}/beneficiaries",
            new { citizenId = "012345678901", fullName = "Người Khác", dateOfBirth = "1990-05-20", email = "a@example.com" });

        await ApiTestHelpers.AssertProblemAsync(conflict, HttpStatusCode.Conflict, "BENEFICIARY_IDENTITY_CONFLICT");
        Assert.Equal(HttpStatusCode.Created, otherVault.StatusCode);
    }

    [Fact]
    public async Task Dms08_NoEmailConfigured_OutsideDevelopment_IsNotDelivered()
    {
        FakeEnvironment production = new FakeEnvironment(Environments.Production);
        FakeEnvironment development = new FakeEnvironment(Environments.Development);
        DeathCaseOwnerNotice notice = new DeathCaseOwnerNotice();
        HandoverInvitation invitation = new HandoverInvitation();
        invitation.Token = "t";

        LogDeathCaseNoticeSender noticeProd = new LogDeathCaseNoticeSender(NullLogger<LogDeathCaseNoticeSender>.Instance, production);
        LogHandoverInvitationSender handoverProd = new LogHandoverInvitationSender(NullLogger<LogHandoverInvitationSender>.Instance, production);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noticeProd.SendAsync(notice, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handoverProd.SendAsync(invitation, CancellationToken.None));

        // Development vẫn chạy được luồng thử, không cần Gmail
        await new LogDeathCaseNoticeSender(NullLogger<LogDeathCaseNoticeSender>.Instance, development).SendAsync(notice, CancellationToken.None);
        await new LogHandoverInvitationSender(NullLogger<LogHandoverInvitationSender>.Instance, development).SendAsync(invitation, CancellationToken.None);
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string name)
        {
            EnvironmentName = name;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "LegacyVault.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
