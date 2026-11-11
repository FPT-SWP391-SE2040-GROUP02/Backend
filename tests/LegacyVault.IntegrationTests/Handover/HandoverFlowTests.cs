using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using LegacyVault.Infrastructure.Storage;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Giai đoạn nhận tài sản qua HTTP trên SQL Server thật: nhận lượt, đồng ý, tải tệp đúng từng byte,
/// chống người khác đoán id, chạy song song và job hết hạn.
/// Chỉ chạy khi có LEGACYVAULT_TEST_CONNECTION; mỗi lớp test dùng một database tạm.
/// </summary>
/// <remarks>
/// Chưa có endpoint mở bàn giao, nên test dựng kho qua API rồi gọi IHandoverStarter trực tiếp.
/// Token mời thật chỉ có hash trong DB, nên test thay hash bằng hash của token đã biết.
/// </remarks>
public sealed class HandoverFlowTests : IAsyncLifetime
{
    private const int FileLength = 200000;
    private const string TextContent = "Mật khẩu két sắt: 123456";

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

    private sealed class Scenario
    {
        public HttpClient Owner { get; set; } = null!;
        public Guid VaultId { get; set; }
        public Guid ContainerId { get; set; }
        public Guid FileAssetId { get; set; }
        public byte[] FileBytes { get; set; } = Array.Empty<byte>();
        public int TurnCount { get; set; }
    }

    private static string TokenFor(int index)
    {
        return "known-token-" + index;
    }

    /// <summary>Dựng kho có 1 tệp 200.000 byte + 1 tài sản chữ trong một gói, rồi mở bàn giao cho beneficiaryCount người nhận.</summary>
    private async Task<Scenario> BuildScenarioAsync(int beneficiaryCount)
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);

        byte[] bytes = new byte[FileLength];
        Random.Shared.NextBytes(bytes);
        using MultipartFormDataContent form = new MultipartFormDataContent();
        ByteArrayContent file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "giay-to.pdf");
        form.Add(new StringContent("Tệp lớn"), "name");
        HttpResponseMessage upload = await owner.PostAsync($"/api/v1/vaults/{vaultId}/assets", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Guid fileAssetId = (await ApiTestHelpers.ReadJsonAsync(upload)).GetProperty("id").GetGuid();

        HttpResponseMessage text = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/assets/text",
            new { name = "Ghi chú", content = TextContent });
        Assert.Equal(HttpStatusCode.Created, text.StatusCode);
        Guid textAssetId = (await ApiTestHelpers.ReadJsonAsync(text)).GetProperty("id").GetGuid();

        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói thừa kế")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, fileAssetId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, textAssetId)).StatusCode);

        // Thứ tự người nhận (theo chỉ số) để gán token và email đúng người, không phụ thuộc thứ tự Id của lượt.
        List<Guid> beneficiaryIds = new List<Guid>();
        for (int i = 0; i < beneficiaryCount; i++)
        {
            string citizenId = "00109000000" + (i + 1);
            JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
                owner, vaultId, citizenId, ApiTestHelpers.BeneficiaryName(i), ReceiverEmail(i));
            HttpResponseMessage designate = await ApiTestHelpers.AddBeneficiaryToContainerAsync(
                owner, vaultId, containerId, beneficiary.GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.OK, designate.StatusCode);
            beneficiaryIds.Add(beneficiary.GetProperty("id").GetGuid());
        }

        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.DeathCaseOpen);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IHandoverStarter starter = scope.ServiceProvider.GetRequiredService<IHandoverStarter>();
            HandoverStartResult result = await starter.StartAsync(vaultId, CancellationToken.None);
            Assert.Equal(beneficiaryCount, result.TurnCount);
        }

        // Thay hash token ngẫu nhiên bằng hash của token đã biết để gọi API.
        ITokenService tokens = factory.Services.GetRequiredService<ITokenService>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<HandoverTurn> turns = await db.Set<HandoverTurn>()
                .Where(t => t.VaultId == vaultId).OrderBy(t => t.Id).ToListAsync();
            foreach (HandoverTurn turn in turns)
            {
                int index = beneficiaryIds.IndexOf(turn.BeneficiaryId);
                turn.InvitationTokenHash = tokens.HashOpaqueToken(TokenFor(index));
            }
            await db.SaveChangesAsync();
        });

        Scenario scenario = new Scenario();
        scenario.Owner = owner;
        scenario.VaultId = vaultId;
        scenario.ContainerId = containerId;
        scenario.FileAssetId = fileAssetId;
        scenario.FileBytes = bytes;
        scenario.TurnCount = beneficiaryCount;
        return scenario;
    }

    /// <summary>Email người nhận thứ index của kịch bản; tài khoản nhận lượt phải đúng email này (D2).</summary>
    private static string ReceiverEmail(int index)
    {
        return "nguoinhan" + index + "@example.com";
    }

    /// <summary>Tài khoản đã xác minh email với đúng email của người nhận thứ index.</summary>
    private Task<HttpClient> NewReceiverAsync(int index = 0)
    {
        return ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, ReceiverEmail(index));
    }

    /// <summary>Tài khoản đã xác minh email nhưng không phải người nhận nào.</summary>
    private Task<HttpClient> NewStrangerAsync()
    {
        return ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
    }

    private static async Task<JsonElement> ClaimAsync(HttpClient client, int tokenIndex)
    {
        HttpResponseMessage response = await client.PostAsync($"/api/v1/handover/invitations/{TokenFor(tokenIndex)}/claim", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    private static async Task<JsonElement> AcceptAsync(HttpClient client, Guid turnId)
    {
        HttpResponseMessage response = await client.PostAsync($"/api/v1/handover/turns/{turnId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    /// <summary>Nhận lượt tokenIndex và đồng ý; trả (turnId, grantId, danh sách tài sản).</summary>
    private static async Task<(Guid TurnId, Guid GrantId, JsonElement Items)> ClaimAndAcceptAsync(HttpClient client, int tokenIndex)
    {
        JsonElement claimed = await ClaimAsync(client, tokenIndex);
        Assert.Equal("READY_TO_ACCEPT", claimed.GetProperty("status").GetString());
        Guid turnId = claimed.GetProperty("id").GetGuid();

        JsonElement accepted = await AcceptAsync(client, turnId);
        Assert.Equal("HANDOVER_COMMITTED", accepted.GetProperty("status").GetString());
        Guid grantId = accepted.GetProperty("grant").GetProperty("id").GetGuid();
        return (turnId, grantId, accepted.GetProperty("items"));
    }

    private static Guid FindItemId(JsonElement items, string kind)
    {
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.GetProperty("kind").GetString() == kind)
            {
                return item.GetProperty("id").GetGuid();
            }
        }
        throw new InvalidOperationException("Không có tài sản loại " + kind);
    }

    [SqlFact]
    public async Task FullFlow_StartClaimAcceptDownload_BytesAreIdentical()
    {
        Scenario scenario = await BuildScenarioAsync(1);
        HttpClient receiver = await NewReceiverAsync();

        (Guid turnId, Guid grantId, JsonElement items) = await ClaimAndAcceptAsync(receiver, 0);

        Assert.Equal(2, items.GetArrayLength());

        Guid fileItemId = FindItemId(items, "FILE");
        HttpResponseMessage download = await receiver.GetAsync($"/api/v1/handover/grants/{grantId}/assets/{fileItemId}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType!.MediaType);
        Assert.Contains("attachment", download.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(scenario.FileBytes, await download.Content.ReadAsByteArrayAsync());

        Guid textItemId = FindItemId(items, "TEXT");
        HttpResponseMessage textDownload = await receiver.GetAsync($"/api/v1/handover/grants/{grantId}/assets/{textItemId}/content");
        Assert.Equal(HttpStatusCode.OK, textDownload.StatusCode);
        Assert.Equal(TextContent, await textDownload.Content.ReadAsStringAsync());

        JsonElement mine = await ApiTestHelpers.ReadJsonAsync(await receiver.GetAsync("/api/v1/handover/turns/me"));
        Assert.Equal(1, mine.GetArrayLength());
        Assert.Equal(turnId, mine[0].GetProperty("id").GetGuid());
        Assert.Equal("ACTIVE", mine[0].GetProperty("grant").GetProperty("status").GetString());

        JsonElement listed = await ApiTestHelpers.ReadJsonAsync(await receiver.GetAsync($"/api/v1/handover/grants/{grantId}/assets"));
        Assert.Equal(2, listed.GetArrayLength());

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            int succeeded = await db.Set<HandoverAccessLog>().CountAsync(l => l.Outcome == HandoverAccessOutcome.Succeeded);
            Assert.Equal(2, succeeded);
        });
    }

    [SqlFact]
    public async Task InvitationEndpoint_IsPublicAndRevealsNothing()
    {
        await BuildScenarioAsync(1);
        HttpClient anonymous = factory.CreateClient();

        HttpResponseMessage ok = await anonymous.GetAsync($"/api/v1/handover/invitations/{TokenFor(0)}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(ok);
        Assert.True(body.GetProperty("valid").GetBoolean());
        Assert.False(body.GetProperty("responseExpired").GetBoolean());
        // Chỉ 5 trường: hai cờ cũ, email đã che bớt, cờ "đã có tài khoản" và tên Chủ kho. Không có email đầy đủ.
        Assert.Equal(5, body.EnumerateObject().Count());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("ownerFullName").GetString()));
        Assert.False(body.GetProperty("accountExists").GetBoolean());
        string masked = body.GetProperty("invitedEmailMasked").GetString()!;
        Assert.EndsWith("@example.com", masked);
        Assert.DoesNotContain(ReceiverEmail(0), await ok.Content.ReadAsStringAsync());

        HttpResponseMessage unknown = await anonymous.GetAsync("/api/v1/handover/invitations/khong-ton-tai");
        await ApiTestHelpers.AssertProblemAsync(unknown, HttpStatusCode.NotFound, "HANDOVER_INVITATION_INVALID");
    }

    [SqlFact]
    public async Task InvitationEndpoint_ReportsAccountExistsOnceRecipientHasAccount()
    {
        await BuildScenarioAsync(1);
        await NewReceiverAsync();

        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/handover/invitations/{TokenFor(0)}");

        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.True(body.GetProperty("accountExists").GetBoolean());
    }

    [SqlFact]
    public async Task MyInvitations_ListsOnlyForRecipientEmail_AndClaimByTurnIdWorks()
    {
        Scenario scenario = await BuildScenarioAsync(2);
        HttpClient receiver = await NewReceiverAsync(0);
        HttpClient stranger = await NewStrangerAsync();

        HttpResponseMessage mine = await receiver.GetAsync("/api/v1/handover/invitations/mine");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        JsonElement list = await ApiTestHelpers.ReadJsonAsync(mine);
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("Gói thừa kế", list[0].GetProperty("containerName").GetString());
        Guid turnId = list[0].GetProperty("turnId").GetGuid();

        JsonElement strangerList = await ApiTestHelpers.ReadJsonAsync(await stranger.GetAsync("/api/v1/handover/invitations/mine"));
        Assert.Equal(0, strangerList.GetArrayLength());

        // Chủ kho không thấy lời mời của kho mình, kể cả khi anonymous bị chặn.
        JsonElement ownerList = await ApiTestHelpers.ReadJsonAsync(await scenario.Owner.GetAsync("/api/v1/handover/invitations/mine"));
        Assert.Equal(0, ownerList.GetArrayLength());
        HttpResponseMessage anonymous = await factory.CreateClient().GetAsync("/api/v1/handover/invitations/mine");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // Sai email: 403 HANDOVER_EMAIL_MISMATCH, không lộ email đầy đủ.
        HttpResponseMessage wrong = await stranger.PostAsync($"/api/v1/handover/turns/{turnId}/claim", null);
        await ApiTestHelpers.AssertProblemAsync(wrong, HttpStatusCode.Forbidden, "HANDOVER_EMAIL_MISMATCH");
        Assert.DoesNotContain(ReceiverEmail(0), await wrong.Content.ReadAsStringAsync());

        // Đúng email: nhận được, gọi lặp trả lại cùng trạng thái, và lượt biến khỏi danh sách chờ.
        HttpResponseMessage claim = await receiver.PostAsync($"/api/v1/handover/turns/{turnId}/claim", null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        JsonElement claimed = await ApiTestHelpers.ReadJsonAsync(claim);
        Assert.Equal(turnId, claimed.GetProperty("id").GetGuid());
        HttpResponseMessage again = await receiver.PostAsync($"/api/v1/handover/turns/{turnId}/claim", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        JsonElement after = await ApiTestHelpers.ReadJsonAsync(await receiver.GetAsync("/api/v1/handover/invitations/mine"));
        Assert.Equal(0, after.GetArrayLength());
    }

    [SqlFact]
    public async Task ClaimByToken_WithWrongEmail_Returns403()
    {
        await BuildScenarioAsync(1);
        HttpClient stranger = await NewStrangerAsync();

        HttpResponseMessage response = await stranger.PostAsync($"/api/v1/handover/invitations/{TokenFor(0)}/claim", null);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "HANDOVER_EMAIL_MISMATCH");
    }

    [SqlFact]
    public async Task OtherUser_GetsSame404ForTurnGrantAndContent()
    {
        await BuildScenarioAsync(1);
        HttpClient receiver = await NewReceiverAsync();
        HttpClient stranger = await NewStrangerAsync();
        (Guid turnId, Guid grantId, JsonElement items) = await ClaimAndAcceptAsync(receiver, 0);
        Guid itemId = FindItemId(items, "FILE");

        string[] realUrls =
        {
            $"/api/v1/handover/turns/{turnId}",
            $"/api/v1/handover/grants/{grantId}/assets",
            $"/api/v1/handover/grants/{grantId}/assets/{itemId}/content"
        };
        string[] unknownUrls =
        {
            $"/api/v1/handover/turns/{Guid.NewGuid()}",
            $"/api/v1/handover/grants/{Guid.NewGuid()}/assets",
            $"/api/v1/handover/grants/{Guid.NewGuid()}/assets/{Guid.NewGuid()}/content"
        };

        for (int i = 0; i < realUrls.Length; i++)
        {
            JsonElement other = await ApiTestHelpers.AssertProblemAsync(
                await stranger.GetAsync(realUrls[i]), HttpStatusCode.NotFound, "HANDOVER_NOT_FOUND");
            JsonElement unknown = await ApiTestHelpers.AssertProblemAsync(
                await stranger.GetAsync(unknownUrls[i]), HttpStatusCode.NotFound, "HANDOVER_NOT_FOUND");
            Assert.Equal(unknown.GetProperty("title").GetString(), other.GetProperty("title").GetString());
        }

        HttpResponseMessage accept = await stranger.PostAsync($"/api/v1/handover/turns/{turnId}/accept", null);
        await ApiTestHelpers.AssertProblemAsync(accept, HttpStatusCode.NotFound, "HANDOVER_NOT_FOUND");
    }

    [SqlFact]
    public async Task ExpiredGrant_ContentIsForbidden()
    {
        await BuildScenarioAsync(1);
        HttpClient receiver = await NewReceiverAsync();
        (Guid _, Guid grantId, JsonElement items) = await ClaimAndAcceptAsync(receiver, 0);
        Guid itemId = FindItemId(items, "FILE");

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset past = DateTimeOffset.UtcNow.AddDays(-1);
            await db.Set<HandoverGrant>().Where(g => g.Id == grantId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.ExpiresAt, past));
        });

        HttpResponseMessage content = await receiver.GetAsync($"/api/v1/handover/grants/{grantId}/assets/{itemId}/content");
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(content, HttpStatusCode.Forbidden, "GRANT_NOT_ACTIVE");
        Assert.Equal("EXPIRED", problem.GetProperty("reason").GetString());

        JsonElement mine = await ApiTestHelpers.ReadJsonAsync(await receiver.GetAsync("/api/v1/handover/turns/me"));
        Assert.Equal("EXPIRED", mine[0].GetProperty("grant").GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task AcceptTwiceInParallel_CreatesOneGrant()
    {
        await BuildScenarioAsync(1);
        HttpClient receiver = await NewReceiverAsync();
        JsonElement claimed = await ClaimAsync(receiver, 0);
        Guid turnId = claimed.GetProperty("id").GetGuid();

        Task<HttpResponseMessage> first = receiver.PostAsync($"/api/v1/handover/turns/{turnId}/accept", null);
        Task<HttpResponseMessage> second = receiver.PostAsync($"/api/v1/handover/turns/{turnId}/accept", null);
        HttpResponseMessage[] responses = await Task.WhenAll(first, second);

        foreach (HttpResponseMessage response in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Conflict,
                "Phản hồi bất ngờ: " + response.StatusCode);
        }
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);

        Guid savedGrantId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<HandoverGrant> grants = await db.Set<HandoverGrant>().Where(g => g.TurnId == turnId).ToListAsync();
            Assert.Single(grants);
            savedGrantId = grants[0].Id;
        });

        JsonElement again = await AcceptAsync(receiver, turnId);
        Assert.Equal(savedGrantId, again.GetProperty("grant").GetProperty("id").GetGuid());
    }

    [SqlFact]
    public async Task TwoBeneficiariesOnSameContainer_AreIndependent()
    {
        await BuildScenarioAsync(2);
        HttpClient receiverA = await NewReceiverAsync();
        HttpClient receiverB = await NewReceiverAsync(1);

        (Guid _, Guid grantA, JsonElement itemsA) = await ClaimAndAcceptAsync(receiverA, 0);
        JsonElement claimedB = await ClaimAsync(receiverB, 1);
        Guid turnB = claimedB.GetProperty("id").GetGuid();
        HttpResponseMessage declined = await receiverB.PostAsync($"/api/v1/handover/turns/{turnB}/decline", null);
        Assert.Equal(HttpStatusCode.OK, declined.StatusCode);
        Assert.Equal("DECLINED", (await ApiTestHelpers.ReadJsonAsync(declined)).GetProperty("status").GetString());

        Guid itemId = FindItemId(itemsA, "FILE");
        HttpResponseMessage bTries = await receiverB.GetAsync($"/api/v1/handover/grants/{grantA}/assets/{itemId}/content");
        await ApiTestHelpers.AssertProblemAsync(bTries, HttpStatusCode.NotFound, "HANDOVER_NOT_FOUND");

        HttpResponseMessage aDownloads = await receiverA.GetAsync($"/api/v1/handover/grants/{grantA}/assets/{itemId}/content");
        Assert.Equal(HttpStatusCode.OK, aDownloads.StatusCode);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<HandoverGrant>().CountAsync());
            PlanStatus plan = await db.HandoverPlans.Select(p => p.Status).SingleAsync();
            Assert.Equal(PlanStatus.Closed, plan);
        });
    }

    [SqlFact]
    public async Task CorruptedEncryptedFile_ReturnsNoContentBytes()
    {
        Scenario scenario = await BuildScenarioAsync(1);
        HttpClient receiver = await NewReceiverAsync();
        (Guid _, Guid grantId, JsonElement items) = await ClaimAndAcceptAsync(receiver, 0);
        Guid fileItemId = FindItemId(items, "FILE");

        string storageKey = string.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            storageKey = await db.Assets
                .Where(a => a.Id == scenario.FileAssetId).Select(a => a.StorageKey).SingleAsync();
        });
        string path = Path.Combine(factory.StorageRoot, storageKey[^2..], storageKey);
        byte[] onDisk = await File.ReadAllBytesAsync(path);
        Assert.True(onDisk.Length > ChunkedAesGcm.HeaderSize + ChunkedAesGcm.ChunkSize);
        onDisk[onDisk.Length - 1] ^= 0x01;
        await File.WriteAllBytesAsync(path, onDisk);

        HttpResponseMessage response = await receiver.GetAsync($"/api/v1/handover/grants/{grantId}/assets/{fileItemId}/content");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        JsonElement problem = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal("CONTENT_INTEGRITY_FAILED", problem.GetProperty("code").GetString());
        Assert.True(body.Length < 4096);
        // Không có đoạn nào của nội dung gốc lọt vào phản hồi.
        byte[] fragment = scenario.FileBytes.AsSpan(1000, 64).ToArray();
        Assert.True(body.AsSpan().IndexOf(fragment) < 0);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<HandoverAccessLog>().CountAsync(l => l.Outcome == HandoverAccessOutcome.IntegrityFailed));
            Assert.Equal(0, await db.Set<HandoverAccessLog>().CountAsync(l => l.Outcome == HandoverAccessOutcome.Succeeded));
        });
    }

    [SqlFact]
    public async Task Database_EnforcesOneGrantPerTurnAndOneActiveTurnPerDesignation()
    {
        await BuildScenarioAsync(1);
        HttpClient receiver = await NewReceiverAsync();
        (Guid turnId, Guid _, JsonElement _) = await ClaimAndAcceptAsync(receiver, 0);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverGrant duplicate = new HandoverGrant
            {
                TurnId = turnId,
                UserId = Guid.NewGuid(),
                Status = HandoverGrantStatus.Active,
                GrantedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
            };
            db.Set<HandoverGrant>().Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverTurn existing = await db.Set<HandoverTurn>().AsNoTracking().SingleAsync(t => t.Id == turnId);
            HandoverTurn second = new HandoverTurn
            {
                VaultId = existing.VaultId,
                PlanId = existing.PlanId,
                ContainerId = existing.ContainerId,
                ContainerName = existing.ContainerName,
                DesignationId = existing.DesignationId,
                BeneficiaryId = existing.BeneficiaryId,
                Status = HandoverTurnStatus.Invited,
                InvitationTokenHash = new string('b', 64),
                InvitedAt = DateTimeOffset.UtcNow,
                ResponseDeadline = DateTimeOffset.UtcNow.AddDays(5),
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Set<HandoverTurn>().Add(second);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    [SqlFact]
    public async Task Jobs_ExpireDecisionAndGrantAndCloseThePlan()
    {
        Scenario scenario = await BuildScenarioAsync(2);
        HttpClient receiverA = await NewReceiverAsync();
        HttpClient receiverB = await NewReceiverAsync(1);
        (Guid _, Guid grantA, JsonElement _) = await ClaimAndAcceptAsync(receiverA, 0);
        JsonElement claimedB = await ClaimAsync(receiverB, 1);
        Guid turnB = claimedB.GetProperty("id").GetGuid();

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DateTimeOffset past = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.Set<HandoverTurn>().Where(t => t.Id == turnB)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DecisionDeadline, past));
            await db.Set<HandoverGrant>().Where(g => g.Id == grantA)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.ExpiresAt, past));
        });

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IHandoverJob job = scope.ServiceProvider.GetRequiredService<IHandoverJob>();
            IReadOnlyList<Guid> decisionIds = await job.FindExpiredDecisionTurnIdsAsync(CancellationToken.None);
            IReadOnlyList<Guid> grantIds = await job.FindExpiredGrantIdsAsync(CancellationToken.None);
            Assert.Equal(turnB, Assert.Single(decisionIds));
            Assert.Equal(grantA, Assert.Single(grantIds));
            await job.ProcessDecisionExpiryAsync(turnB, CancellationToken.None);
        }
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IHandoverJob job = scope.ServiceProvider.GetRequiredService<IHandoverJob>();
            await job.ProcessGrantExpiryAsync(grantA, CancellationToken.None);
        }

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverTurnStatus statusB = await db.Set<HandoverTurn>().Where(t => t.Id == turnB).Select(t => t.Status).SingleAsync();
            HandoverGrantStatus grantStatus = await db.Set<HandoverGrant>().Where(g => g.Id == grantA).Select(g => g.Status).SingleAsync();
            PlanStatus plan = await db.HandoverPlans.Where(p => p.VaultId == scenario.VaultId).Select(p => p.Status).SingleAsync();
            Assert.Equal(HandoverTurnStatus.DecisionExpired, statusB);
            Assert.Equal(HandoverGrantStatus.Expired, grantStatus);
            Assert.Equal(PlanStatus.Closed, plan);
        });
    }
}
