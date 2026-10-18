using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Executor mở hồ sơ chứng tử, xem và sửa bản nháp qua HTTP trên SQL Server thật (docs/api-contract.md mục 6.10).
/// Mỗi lớp test dùng một database tạm (xem ContainerFlowTests).
/// </summary>
public sealed class DeathCaseOpenFlowTests : IAsyncLifetime
{
    private const string CitizenId = "012345678901";

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
    public async Task Ac07_Death01_ExecutorOpensCaseBeforeCheckInDue()
    {
        // Chuẩn bị: kế hoạch đang chạy, Executor đã chấp nhận, Owner chưa quá hạn điểm danh
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        // Hành động
        HttpResponseMessage response = await executor.Client.PostAsync(OpenUrl(vault.VaultId), null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("DRAFT", body.GetProperty("status").GetString());
        Assert.Equal(vault.VaultId, body.GetProperty("vaultId").GetGuid());
        JsonElement version = body.GetProperty("currentVersion");
        Assert.Equal(1, version.GetProperty("versionNo").GetInt32());
        Assert.True(version.GetProperty("isDraft").GetBoolean());
        Assert.Equal(0, version.GetProperty("files").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reviewVersionNo").ValueKind);
        List<string> missing = ReadStrings(body.GetProperty("missingRequirements"));
        Assert.Equal(new List<string>
        {
            "OWNER_FULL_NAME", "OWNER_CITIZEN_ID", "DATE_OF_DEATH",
            "CERTIFICATE_NUMBER", "ISSUED_ON", "ISSUED_BY", "FILES",
        }, missing);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("etag").GetString()));
        // Mở hồ sơ nháp chưa khóa kế hoạch (D1).
        await AssertPlanStatusAsync(vault.VaultId, PlanStatus.Active);
    }

    [SqlFact]
    public async Task Death05_DraftPlanCannotOpenCase()
    {
        // Chuẩn bị: kế hoạch chưa kích hoạt
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.Draft);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        // Hành động
        HttpResponseMessage response = await executor.Client.PostAsync(OpenUrl(vault.VaultId), null);

        // Kiểm tra
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("PLAN_NOT_RUNNING", problem.GetProperty("reason").GetString());
        Assert.Equal("DRAFT", problem.GetProperty("planStatus").GetString());
        Assert.Equal(0, await CountCasesAsync(vault.VaultId));
    }

    [SqlFact]
    public async Task Death05_GraceReadOnlyVaultCanStillOpenCase()
    {
        // Chuẩn bị: dịch vụ đang ân hạn (chỉ đọc) nhưng kế hoạch vẫn chạy
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await ApiTestHelpers.SetEntitlementStatusAsync(factory, vault.VaultId, EntitlementStatus.GraceReadOnly);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        // Hành động
        HttpResponseMessage response = await executor.Client.PostAsync(OpenUrl(vault.VaultId), null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountCasesAsync(vault.VaultId));
    }

    [SqlFact]
    public async Task Ac71_Ac91_TwoExecutorsOpenAtOnceGetOneCase()
    {
        // Chuẩn bị: hai Executor của cùng một kho
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient first = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        DeathCaseTestData.ExecutorClient second = await DeathCaseTestData.CreateExecutorClientAsync(
            factory, vault.VaultId, ExecutorRole.Backup);

        // Hành động: mở cùng lúc
        Task<HttpResponseMessage> firstTask = first.Client.PostAsync(OpenUrl(vault.VaultId), null);
        Task<HttpResponseMessage> secondTask = second.Client.PostAsync(OpenUrl(vault.VaultId), null);
        HttpResponseMessage[] responses = await Task.WhenAll(firstTask, secondTask);

        // Kiểm tra: một hồ sơ duy nhất; một bên 201, bên kia 200 và cùng Id
        int createdCount = 0;
        int okCount = 0;
        List<Guid> ids = new List<Guid>();
        foreach (HttpResponseMessage response in responses)
        {
            if (response.StatusCode == HttpStatusCode.Created)
            {
                createdCount++;
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                okCount++;
            }
            JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
            ids.Add(body.GetProperty("id").GetGuid());
        }
        Assert.Equal(1, createdCount);
        Assert.Equal(1, okCount);
        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(1, await CountCasesAsync(vault.VaultId));
    }

    [SqlFact]
    public async Task Death05_SecondOpenReturnsExistingCase()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement firstBody = await ApiTestHelpers.ReadJsonAsync(await executor.Client.PostAsync(OpenUrl(vault.VaultId), null));

        // Hành động
        HttpResponseMessage response = await executor.Client.PostAsync(OpenUrl(vault.VaultId), null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(firstBody.GetProperty("id").GetGuid(), body.GetProperty("id").GetGuid());
        Assert.Equal(1, await CountCasesAsync(vault.VaultId));
    }

    [SqlFact]
    public async Task Sec04_NonExecutorAndOwnerGetNotFound()
    {
        // Chuẩn bị: một hồ sơ có sẵn; người ngoài và chính Owner đều không phải Executor
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        Guid caseId = await OpenCaseAsync(executor.Client, vault.VaultId);
        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động
        HttpResponseMessage strangerOpen = await stranger.PostAsync(OpenUrl(vault.VaultId), null);
        HttpResponseMessage ownerOpen = await vault.Client.PostAsync(OpenUrl(vault.VaultId), null);
        HttpResponseMessage strangerGet = await stranger.GetAsync(CaseUrl(caseId));
        HttpResponseMessage ownerGet = await vault.Client.GetAsync(CaseUrl(caseId));
        HttpResponseMessage strangerPut = await stranger.PutAsJsonAsync(CaseUrl(caseId) + "/draft", new { etag = "\"0\"" });
        HttpResponseMessage missingGet = await executor.Client.GetAsync(CaseUrl(Guid.NewGuid()));

        // Kiểm tra: 404 như nhau, không lộ hồ sơ có tồn tại hay không
        await ApiTestHelpers.AssertProblemAsync(strangerOpen, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(ownerOpen, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(strangerGet, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(ownerGet, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(strangerPut, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(missingGet, HttpStatusCode.NotFound, "DEATH_CASE_NOT_FOUND");
    }

    [SqlFact]
    public async Task Sec04_InvitedButNotAcceptedExecutorGetsNotFound()
    {
        // Chuẩn bị: được mời nhưng chưa chấp nhận; và một người đã bị thu hồi
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        User invited = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        User revoked = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await DeathCaseTestData.AddExecutorAsync(factory, vault.VaultId, invited.Id, ExecutorStatus.Invited, ExecutorRole.Primary);
        await DeathCaseTestData.AddExecutorAsync(factory, vault.VaultId, revoked.Id, ExecutorStatus.Revoked, ExecutorRole.Backup);
        HttpClient invitedClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(invitedClient, factory.Services, invited);
        HttpClient revokedClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(revokedClient, factory.Services, revoked);

        // Hành động
        HttpResponseMessage invitedOpen = await invitedClient.PostAsync(OpenUrl(vault.VaultId), null);
        HttpResponseMessage revokedOpen = await revokedClient.PostAsync(OpenUrl(vault.VaultId), null);

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(invitedOpen, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(revokedOpen, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        Assert.Equal(0, await CountCasesAsync(vault.VaultId));
    }

    [SqlFact]
    public async Task Death04_DraftEditRejectsStaleEtag()
    {
        // Chuẩn bị: hai Executor cùng đọc một etag; người thứ nhất sửa trước
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient first = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        DeathCaseTestData.ExecutorClient second = await DeathCaseTestData.CreateExecutorClientAsync(
            factory, vault.VaultId, ExecutorRole.Backup);
        JsonElement opened = await ApiTestHelpers.ReadJsonAsync(await first.Client.PostAsync(OpenUrl(vault.VaultId), null));
        Guid caseId = opened.GetProperty("id").GetGuid();
        string staleEtag = opened.GetProperty("etag").GetString()!;
        HttpResponseMessage firstEdit = await first.Client.PutAsJsonAsync(
            CaseUrl(caseId) + "/draft", new { etag = staleEtag, ownerFullName = "Nguyễn Văn An" });
        Assert.Equal(HttpStatusCode.OK, firstEdit.StatusCode);

        // Hành động: người thứ hai sửa bằng etag cũ
        HttpResponseMessage secondEdit = await second.Client.PutAsJsonAsync(
            CaseUrl(caseId) + "/draft", new { etag = staleEtag, ownerFullName = "Người khác" });

        // Kiểm tra
        await ApiTestHelpers.AssertProblemAsync(secondEdit, HttpStatusCode.Conflict, "EDIT_CONFLICT");
        JsonElement current = await ApiTestHelpers.ReadJsonAsync(await second.Client.GetAsync(CaseUrl(caseId)));
        Assert.Equal("Nguyễn Văn An", current.GetProperty("currentVersion").GetProperty("ownerFullName").GetString());
        Assert.NotEqual(staleEtag, current.GetProperty("etag").GetString());
    }

    [SqlFact]
    public async Task Data02_ResponseShowsOnlyCitizenIdLast4()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement opened = await ApiTestHelpers.ReadJsonAsync(await executor.Client.PostAsync(OpenUrl(vault.VaultId), null));
        Guid caseId = opened.GetProperty("id").GetGuid();
        DateOnly today = DeathCaseRulesToday();

        // Hành động: gõ CCCD có khoảng trắng; khai đủ các trường
        HttpResponseMessage response = await executor.Client.PutAsJsonAsync(CaseUrl(caseId) + "/draft", new
        {
            etag = opened.GetProperty("etag").GetString(),
            ownerFullName = "  Nguyễn Văn An ",
            ownerCitizenId = "0123 4567 8901",
            dateOfDeath = today.AddDays(-3).ToString("yyyy-MM-dd"),
            certificateNumber = "123/2026/TT",
            issuedOn = today.AddDays(-2).ToString("yyyy-MM-dd"),
            issuedBy = "UBND phường Bến Nghé",
        });

        // Kiểm tra: response không có CCCD đầy đủ; DB không chứa CCCD dạng rõ
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(CitizenId, raw);
        JsonElement body = JsonDocument.Parse(raw).RootElement;
        JsonElement version = body.GetProperty("currentVersion");
        Assert.Equal("8901", version.GetProperty("ownerCitizenIdLast4").GetString());
        Assert.Equal("Nguyễn Văn An", version.GetProperty("ownerFullName").GetString());
        Assert.Equal("123/2026/TT", version.GetProperty("certificateNumber").GetString());
        Assert.Equal(today.AddDays(-3).ToString("yyyy-MM-dd"), version.GetProperty("dateOfDeath").GetString());
        // Đủ 6 trường, còn thiếu trang giấy.
        Assert.Equal(new List<string> { "FILES" }, ReadStrings(body.GetProperty("missingRequirements")));
        byte[]? encrypted = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseVersion stored = await db.Set<DeathCaseVersion>().SingleAsync(v => v.CaseId == caseId);
            encrypted = stored.IdentityEncrypted;
        });
        Assert.NotNull(encrypted);
        Assert.False(encrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes(CitizenId)) >= 0);
    }

    [SqlFact]
    public async Task Death01_InvalidDatesAndCitizenIdAreRejected()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        JsonElement opened = await ApiTestHelpers.ReadJsonAsync(await executor.Client.PostAsync(OpenUrl(vault.VaultId), null));
        Guid caseId = opened.GetProperty("id").GetGuid();
        string etag = opened.GetProperty("etag").GetString()!;
        DateOnly today = DeathCaseRulesToday();

        // Hành động
        HttpResponseMessage futureDeath = await executor.Client.PutAsJsonAsync(
            CaseUrl(caseId) + "/draft", new { etag, dateOfDeath = today.AddDays(1).ToString("yyyy-MM-dd") });
        HttpResponseMessage shortCitizenId = await executor.Client.PutAsJsonAsync(
            CaseUrl(caseId) + "/draft", new { etag, ownerCitizenId = "123" });

        // Kiểm tra
        JsonElement futureProblem = await ApiTestHelpers.AssertProblemAsync(futureDeath, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(futureProblem.GetProperty("errors").TryGetProperty("dateOfDeath", out _));
        JsonElement idProblem = await ApiTestHelpers.AssertProblemAsync(shortCitizenId, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(idProblem.GetProperty("errors").TryGetProperty("ownerCitizenId", out _));
    }

    [SqlFact]
    public async Task Dms05_ExecutorVaultListShowsOverdueFlag()
    {
        // Chuẩn bị: user là Executor của hai kho; kho 2 đã quá hạn điểm danh
        DeathCaseTestData.OwnerVault vault1 = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.OwnerVault vault2 = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault2.VaultId, PlanStatus.CheckinOverdue);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault1.VaultId);
        await DeathCaseTestData.AddAcceptedExecutorAsync(factory, vault2.VaultId, executor.User.Id, ExecutorRole.Backup);
        Guid caseId = await OpenCaseAsync(executor.Client, vault1.VaultId);

        // Hành động
        HttpResponseMessage response = await executor.Client.GetAsync("/api/v1/executor/vaults");

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement list = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(2, list.GetArrayLength());
        JsonElement? row1 = FindVault(list, vault1.VaultId);
        JsonElement? row2 = FindVault(list, vault2.VaultId);
        Assert.NotNull(row1);
        Assert.NotNull(row2);
        Assert.False(row1.Value.GetProperty("checkInOverdue").GetBoolean());
        Assert.Equal("PRIMARY", row1.Value.GetProperty("role").GetString());
        Assert.Equal("ACTIVE", row1.Value.GetProperty("planStatus").GetString());
        Assert.Equal(caseId, row1.Value.GetProperty("openDeathCaseId").GetGuid());
        Assert.True(row2.Value.GetProperty("checkInOverdue").GetBoolean());
        Assert.Equal("BACKUP", row2.Value.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, row2.Value.GetProperty("openDeathCaseId").ValueKind);
        Assert.Equal("Người thử", row2.Value.GetProperty("ownerFullName").GetString());
    }

    private static string OpenUrl(Guid vaultId)
    {
        return $"/api/v1/executor/vaults/{vaultId}/death-case";
    }

    private static string CaseUrl(Guid caseId)
    {
        return $"/api/v1/executor/death-cases/{caseId}";
    }

    /// <summary>Ngày hôm nay theo giờ Việt Nam, giống cách service tính.</summary>
    private static DateOnly DeathCaseRulesToday()
    {
        return LegacyVault.Application.Services.DeathCaseRules.TodayInVietnam(DateTimeOffset.UtcNow);
    }

    private static async Task<Guid> OpenCaseAsync(HttpClient client, Guid vaultId)
    {
        HttpResponseMessage response = await client.PostAsync(OpenUrl(vaultId), null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        return body.GetProperty("id").GetGuid();
    }

    private static List<string> ReadStrings(JsonElement array)
    {
        List<string> values = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            values.Add(item.GetString()!);
        }
        return values;
    }

    private static JsonElement? FindVault(JsonElement list, Guid vaultId)
    {
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.GetProperty("vaultId").GetGuid() == vaultId)
            {
                return item;
            }
        }
        return null;
    }

    private async Task<int> CountCasesAsync(Guid vaultId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Set<DeathCase>().CountAsync(c => c.VaultId == vaultId);
        });
        return count;
    }

    private async Task AssertPlanStatusAsync(Guid vaultId, PlanStatus expected)
    {
        PlanStatus actual = PlanStatus.Closed;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            actual = await db.HandoverPlans.Where(p => p.VaultId == vaultId).Select(p => p.Status).SingleAsync();
        });
        Assert.Equal(expected, actual);
    }
}
