using System.Net;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Admin;

/// <summary>
/// Chi tiết tài khoản cho Admin (SQL Server thật): kho mà Người thực thi đang giữ và việc đang giữ của luật sư theo luồng
/// bàn giao mới (biên bản, hồ sơ báo tin). Không lộ CCCD, tên tài sản hay email người nhận.
/// </summary>
public sealed class AdminUserDetailWorkTests : IAsyncLifetime
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
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        return client;
    }

    private static async Task<JsonElement> ReadDetailAsync(HttpClient admin, Guid userId)
    {
        HttpResponseMessage response = await admin.GetAsync("/api/v1/admin/users/" + userId);
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, "detail -> " + response.StatusCode + ": " + text);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    /// <summary>Kho có một người nhận được gán một tài sản tên "Nhà"; email người nhận trả về để kiểm không bị lộ.</summary>
    private async Task<(Guid VaultId, string RecipientEmail)> BuildVaultWithRecipientAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid asset = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        string email = "giu-kho-" + vaultId.ToString("N").Substring(0, 8) + "@example.com";
        JsonElement recipient = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000051", ApiTestHelpers.BeneficiaryName(0), email);
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, asset, recipient.GetProperty("id").GetGuid());
        return (vaultId, email);
    }

    [SqlFact]
    public async Task Executor_HeldVaults_ListsOnlyVaultsWhereThisPersonIsTheCurrentExecutor()
    {
        // Chuẩn bị: kho A đang bàn giao (đã chốt biên bản), kho B đang chạy; kho C người này đã bị thu hồi;
        // kho D do Người thực thi khác giữ
        HttpClient admin = await AdminClientAsync();
        User executor = await VaultTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        User other = await VaultTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        (Guid vaultA, string recipientEmail) = await BuildVaultWithRecipientAsync();
        HandoverRecordTestData.FinalizedRecord finalized = await HandoverRecordTestData.FinalizeFromOwnerSplitAsync(factory, vaultA);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            // Đổi Người thực thi của kho A sang tài khoản cần xem (người tạo sẵn bị thu hồi).
            Executor current = await db.Set<Executor>().SingleAsync(e => e.VaultId == vaultA && e.Status == ExecutorStatus.Accepted);
            current.Status = ExecutorStatus.Revoked;
            await db.SaveChangesAsync();
        });
        await VaultTestData.AddAcceptedExecutorAsync(factory, vaultA, executor.Id);
        VaultTestData.OwnerVault vaultB = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        await VaultTestData.AddAcceptedExecutorAsync(factory, vaultB.VaultId, executor.Id);
        VaultTestData.OwnerVault vaultC = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        await VaultTestData.AddExecutorAsync(factory, vaultC.VaultId, executor.Id, ExecutorStatus.Revoked);
        VaultTestData.OwnerVault vaultD = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        await VaultTestData.AddAcceptedExecutorAsync(factory, vaultD.VaultId, other.Id);

        // Hành động
        JsonElement detail = await ReadDetailAsync(admin, executor.Id);
        HttpResponseMessage raw = await admin.GetAsync("/api/v1/admin/users/" + executor.Id);
        string text = await raw.Content.ReadAsStringAsync();

        // Kiểm tra: đúng hai kho, đúng trạng thái; không lộ CCCD, tên tài sản, email người nhận
        JsonElement held = detail.GetProperty("heldVaults");
        Assert.Equal(2, held.GetArrayLength());
        JsonElement rowA = held.EnumerateArray().Single(v => v.GetProperty("vaultId").GetGuid() == vaultA);
        JsonElement rowB = held.EnumerateArray().Single(v => v.GetProperty("vaultId").GetGuid() == vaultB.VaultId);
        Assert.Equal("HANDOVER_IN_PROGRESS", rowA.GetProperty("planStatus").GetString());
        Assert.Equal("SIGNING", rowA.GetProperty("handoverRecordStatus").GetString());
        Assert.True(rowA.GetProperty("handoverUnderway").GetBoolean());
        Assert.True(rowA.GetProperty("canChangeExecutor").GetBoolean());
        Assert.Equal("ACTIVE", rowB.GetProperty("planStatus").GetString());
        Assert.Equal(JsonValueKind.Null, rowB.GetProperty("handoverRecordStatus").ValueKind);
        Assert.False(rowB.GetProperty("handoverUnderway").GetBoolean());
        Assert.Matches("^(\\p{Lu}\\. ?)+$", rowA.GetProperty("ownerNameMasked").GetString());
        Assert.DoesNotContain("citizenId", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nhà", text);
        Assert.DoesNotContain(recipientEmail, text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, detail.GetProperty("assignedCases").GetArrayLength());
        Assert.NotNull(finalized);
    }

    [SqlFact]
    public async Task ClosedPlan_CannotChangeExecutor_AndOtherRolesHaveNoHeldVaults()
    {
        // Chuẩn bị
        HttpClient admin = await AdminClientAsync();
        User executor = await VaultTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        User ordinary = await VaultTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        VaultTestData.OwnerVault closed = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        await VaultTestData.AddAcceptedExecutorAsync(factory, closed.VaultId, executor.Id);
        await VaultTestData.AddAcceptedExecutorAsync(factory, closed.VaultId, ordinary.Id);
        await ApiTestHelpers.SetPlanStatusAsync(factory, closed.VaultId, PlanStatus.Closed);

        // Hành động
        JsonElement executorDetail = await ReadDetailAsync(admin, executor.Id);
        JsonElement ordinaryDetail = await ReadDetailAsync(admin, ordinary.Id);

        // Kiểm tra
        JsonElement row = Assert.Single(executorDetail.GetProperty("heldVaults").EnumerateArray());
        Assert.False(row.GetProperty("canChangeExecutor").GetBoolean());
        Assert.False(row.GetProperty("handoverUnderway").GetBoolean());
        Assert.Equal(0, ordinaryDetail.GetProperty("heldVaults").GetArrayLength());
    }

    [SqlFact]
    public async Task Verifier_AssignedCasesAndRecentDecisions_ComeFromHandoverRecords()
    {
        // Chuẩn bị: luật sư nhận yêu cầu, chấp nhận giấy tờ và chốt biên bản
        HttpClient admin = await AdminClientAsync();
        (Guid vaultId, string _) = await BuildVaultWithRecipientAsync();
        HandoverRecordTestData.FinalizedRecord finalized = await HandoverRecordTestData.FinalizeFromOwnerSplitAsync(factory, vaultId);

        // Hành động
        JsonElement detail = await ReadDetailAsync(admin, finalized.Verifier.User.Id);

        // Kiểm tra: một biên bản đang giao; một lần xét đã chấp nhận; không có lý do
        JsonElement caseRow = Assert.Single(detail.GetProperty("assignedCases").EnumerateArray());
        Assert.Equal("HANDOVER_RECORD", caseRow.GetProperty("kind").GetString());
        Assert.Equal(finalized.RecordId, caseRow.GetProperty("caseId").GetGuid());
        Assert.Equal(HandoverRecordRules.CaseCode(finalized.RecordId), caseRow.GetProperty("caseCode").GetString());
        Assert.Equal("SIGNING", caseRow.GetProperty("status").GetString());
        JsonElement decision = Assert.Single(detail.GetProperty("recentDecisions").EnumerateArray());
        Assert.Equal("HANDOVER_RECORD", decision.GetProperty("kind").GetString());
        Assert.Equal("ACCEPTED", decision.GetProperty("decision").GetString());
        Assert.False(decision.TryGetProperty("reason", out JsonElement _));
        Assert.Equal(0, detail.GetProperty("heldVaults").GetArrayLength());
    }

    [Fact]
    public void MaskFullName_KeepsOnlyTheFirstLetterOfEachWord()
    {
        // Chuẩn bị, Hành động
        string masked = AdminService.MaskFullName("  Nguyễn  văn An ");

        // Kiểm tra
        Assert.Equal("N. V. A.", masked);
    }
}
