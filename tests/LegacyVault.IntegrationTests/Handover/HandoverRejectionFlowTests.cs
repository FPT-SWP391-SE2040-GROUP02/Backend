using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Luật sư từ chối yêu cầu nhận tài sản hoặc hồ sơ báo tin (SQL Server thật): từ chối chỉ đóng đúng yêu cầu, hồ sơ đó;
/// người bị từ chối gửi lại được; biên bản chưa khóa chỉ đóng khi cả hai đường không còn gì chờ xét.
/// </summary>
public sealed class HandoverRejectionFlowTests : IAsyncLifetime
{
    private const string RequestsUrl = "/api/v1/handover/requests";
    private const string ReportsUrl = "/api/v1/handover/reports";
    private const string VerifierReportsUrl = "/api/v1/verifier/handover-reports";

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

    private sealed class RejectionVault
    {
        public Guid VaultId { get; set; }
        public string OwnerCitizenId { get; set; } = string.Empty;
        public Guid FirstRecipient { get; set; }
        public HttpClient FirstClient { get; set; } = null!;
        public HttpClient SecondClient { get; set; } = null!;
    }

    /// <summary>
    /// Kho đang hoạt động: hai người nhận đã có tài khoản, có Người thực thi, một luật sư rảnh, Chủ tài sản đã eKYC (để
    /// hồ sơ báo tin tìm ra kho theo số căn cước).
    /// </summary>
    private async Task<RejectionVault> BuildVaultAsync()
    {
        string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        string citizenId = "0010" + Random.Shared.Next(10000000, 99999999);
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        JsonElement first = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000031", ApiTestHelpers.BeneficiaryName(0), "tuchoi-" + suffix + "-0@example.com");
        JsonElement second = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000032", ApiTestHelpers.BeneficiaryName(1), "tuchoi-" + suffix + "-1@example.com");
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetId, first.GetProperty("id").GetGuid());
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        await VaultTestData.CreateExecutorClientAsync(factory, vaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IOwnerIdentityProtector protector = scope.ServiceProvider.GetRequiredService<IOwnerIdentityProtector>();
            await ApiTestHelpers.WithDbAsync(factory, async db =>
            {
                Guid ownerId = await db.Set<Vault>().Where(v => v.Id == vaultId).Select(v => v.OwnerId).SingleAsync();
                OwnerIdentity identity = new OwnerIdentity();
                identity.UserId = ownerId;
                identity.SourceVerificationId = Guid.NewGuid();
                identity.CreatedAt = DateTimeOffset.UtcNow;
                OwnerIdentityData data = new OwnerIdentityData();
                data.CitizenId = citizenId;
                data.FullName = "NGUYỄN VĂN MẤT";
                data.DateOfBirth = new DateOnly(1950, 1, 1);
                protector.Protect(identity, data);
                db.Add(identity);
                await db.SaveChangesAsync();
            });
        }

        RejectionVault result = new RejectionVault();
        result.VaultId = vaultId;
        result.OwnerCitizenId = citizenId;
        result.FirstRecipient = first.GetProperty("id").GetGuid();
        result.FirstClient = await ApiTestHelpers.CreateClientForNewUserAsync(
            factory, emailVerified: true, "tuchoi-" + suffix + "-0@example.com");
        result.SecondClient = await ApiTestHelpers.CreateClientForNewUserAsync(
            factory, emailVerified: true, "tuchoi-" + suffix + "-1@example.com");
        return result;
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, response.StatusCode + ": " + text);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    private static async Task<(Guid ClaimId, Guid RecordId)> SubmitClaimAsync(HttpClient recipient, Guid vaultId)
    {
        JsonElement claim = await OkJsonAsync(await recipient.PostAsync(
            RequestsUrl + "/by-account", HandoverRecordTestData.ByAccountForm(vaultId, "Người thân mất")));
        return (claim.GetProperty("id").GetGuid(), claim.GetProperty("recordId").GetGuid());
    }

    private static async Task<Guid> SubmitReportAsync(HttpClient client, string citizenId)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new StringContent("Nguyễn Văn Mất"), "deceasedFullName");
        form.Add(new StringContent(citizenId), "deceasedCitizenId");
        form.Add(new StringContent("true"), "attested");
        form.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "deathCertificates", "chung-tu.pdf");
        form.Add(new ByteArrayContent(HandoverRecordTestData.SamplePdf()), "wills", "di-chuc.pdf");
        JsonElement report = await OkJsonAsync(await client.PostAsync(ReportsUrl, form));
        return report.GetProperty("id").GetGuid();
    }

    private async Task ReviewClaimAsync(Guid recordId, Guid claimId, string decision, string? reason)
    {
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        await OkJsonAsync(await verifier.Client.PostAsJsonAsync(
            HandoverRecordTestData.VerifierRecordUrl(recordId) + "/claims/" + claimId + "/review", new { decision, reason }));
    }

    /// <summary>Luật sư được giao hồ sơ xét hồ sơ đó.</summary>
    private async Task ReviewReportAsync(Guid reportId, string decision, string? reason)
    {
        User? user = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Guid verifierId = await db.Set<HandoverReport>().Where(r => r.Id == reportId).Select(r => r.VerifierUserId).SingleAsync();
            user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == verifierId);
        });
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user!);
        await OkJsonAsync(await client.PostAsJsonAsync(
            $"{VerifierReportsUrl}/{reportId}/review", new { decision, reason, confirmDocuments = true }));
    }

    private async Task<HandoverRecordStatus> ReadRecordStatusAsync(Guid recordId)
    {
        HandoverRecordStatus status = HandoverRecordStatus.Drafting;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            status = await db.Set<HandoverRecord>().Where(r => r.Id == recordId).Select(r => r.Status).SingleAsync();
        });
        return status;
    }

    private async Task<Guid?> ReadReportRecordAsync(Guid reportId)
    {
        Guid? recordId = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            recordId = await db.Set<HandoverReport>().Where(r => r.Id == reportId).Select(r => r.RecordId).SingleAsync();
        });
        return recordId;
    }

    private static async Task<JsonElement> AvailableRowAsync(HttpClient recipient, Guid vaultId)
    {
        JsonElement rows = await OkJsonAsync(await recipient.GetAsync(RequestsUrl + "/available"));
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.GetProperty("vaultId").GetGuid() == vaultId)
            {
                return row;
            }
        }
        throw new InvalidOperationException("Không thấy kho trong danh sách Nhận tài sản.");
    }

    [SqlFact]
    public async Task RejectedRequest_DoesNotBlockAReport_FromTheSamePersonOrAnother()
    {
        // Chuẩn bị: yêu cầu duy nhất bị từ chối
        RejectionVault vault = await BuildVaultAsync();
        (Guid claimId, Guid recordId) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        await ReviewClaimAsync(recordId, claimId, "REJECT", "Giấy tờ không đúng");
        HttpClient relative = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động: chính người bị từ chối và một người thân khác nộp hồ sơ báo tin; luật sư chấp nhận hồ sơ của người thân
        Guid ownReport = await SubmitReportAsync(vault.FirstClient, vault.OwnerCitizenId);
        Guid relativeReport = await SubmitReportAsync(relative, vault.OwnerCitizenId);
        await ReviewReportAsync(relativeReport, "ACCEPT", "Đã đối chiếu");

        // Kiểm tra: biên bản cũ đã đóng, hồ sơ được chấp nhận mở biên bản mới
        Assert.Equal(HandoverRecordStatus.Cancelled, await ReadRecordStatusAsync(recordId));
        Guid? newRecord = await ReadReportRecordAsync(relativeReport);
        Assert.NotNull(newRecord);
        Assert.NotEqual(recordId, newRecord!.Value);
        Assert.NotEqual(Guid.Empty, ownReport);
    }

    [SqlFact]
    public async Task RejectedReport_DoesNotBlockARequest()
    {
        // Chuẩn bị: hồ sơ báo tin của người thân bị từ chối
        RejectionVault vault = await BuildVaultAsync();
        HttpClient relative = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid reportId = await SubmitReportAsync(relative, vault.OwnerCitizenId);
        await ReviewReportAsync(reportId, "REJECT", "Chứng tử không hợp lệ");

        // Hành động
        (Guid _, Guid recordId) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        JsonElement row = await AvailableRowAsync(vault.FirstClient, vault.VaultId);

        // Kiểm tra
        Assert.Equal(HandoverRecordStatus.Drafting, await ReadRecordStatusAsync(recordId));
        Assert.Equal("REQUEST_PENDING", row.GetProperty("status").GetString());
    }

    [SqlFact]
    public async Task RejectedRecipient_ResubmitsWhileAnotherIsWaiting_ReopensTheSameRequest()
    {
        // Chuẩn bị: hai người nhận cùng gửi, người đầu bị từ chối, người sau còn chờ
        RejectionVault vault = await BuildVaultAsync();
        (Guid firstClaim, Guid recordId) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        await SubmitClaimAsync(vault.SecondClient, vault.VaultId);
        await ReviewClaimAsync(recordId, firstClaim, "REJECT", "Thiếu bản gốc");
        JsonElement rejected = await AvailableRowAsync(vault.FirstClient, vault.VaultId);

        // Hành động: người bị từ chối gửi lại kèm giấy tờ mới
        (Guid againClaim, Guid againRecord) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        JsonElement pending = await AvailableRowAsync(vault.FirstClient, vault.VaultId);
        JsonElement mine = (await OkJsonAsync(await vault.FirstClient.GetAsync(RequestsUrl + "/mine")))[0];

        // Kiểm tra: trước khi gửi lại hiện bị từ chối và gửi được; sau đó cùng yêu cầu quay về chờ xét, chỉ còn tệp mới
        Assert.Equal("REJECTED", rejected.GetProperty("status").GetString());
        Assert.True(rejected.GetProperty("canRequest").GetBoolean());
        Assert.Equal("Thiếu bản gốc", rejected.GetProperty("reviewReason").GetString());
        Assert.Equal(firstClaim, againClaim);
        Assert.Equal(recordId, againRecord);
        Assert.Equal("REQUEST_PENDING", pending.GetProperty("status").GetString());
        Assert.False(pending.GetProperty("canRequest").GetBoolean());
        Assert.Equal("SUBMITTED", mine.GetProperty("status").GetString());
        Assert.Equal(2, mine.GetProperty("files").GetArrayLength());
        int logs = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            logs = await db.Set<AuditLog>().CountAsync(
                l => l.VaultId == vault.VaultId && l.Action == AuditAction.HandoverClaimResubmitted);
        });
        Assert.Equal(1, logs);
    }

    [SqlFact]
    public async Task EveryRequestRejected_ClosesTheRecord_AndTheRecipientSeesRejectedWithResubmit()
    {
        // Chuẩn bị
        RejectionVault vault = await BuildVaultAsync();
        (Guid claimId, Guid recordId) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        await ReviewClaimAsync(recordId, claimId, "REJECT", "Không đúng người");
        JsonElement rejected = await AvailableRowAsync(vault.FirstClient, vault.VaultId);

        // Hành động
        (Guid newClaim, Guid newRecord) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);

        // Kiểm tra
        Assert.Equal(HandoverRecordStatus.Cancelled, await ReadRecordStatusAsync(recordId));
        Assert.Equal("REJECTED", rejected.GetProperty("status").GetString());
        Assert.True(rejected.GetProperty("canRequest").GetBoolean());
        Assert.Equal("Không đúng người", rejected.GetProperty("reviewReason").GetString());
        Assert.NotEqual(recordId, newRecord);
        Assert.NotEqual(claimId, newClaim);
    }

    [SqlFact]
    public async Task RejectingAnotherAfterAnAcceptance_KeepsTheRecordOpen()
    {
        // Chuẩn bị: người sau được chấp nhận nên kho đã khóa
        RejectionVault vault = await BuildVaultAsync();
        (Guid firstClaim, Guid recordId) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        (Guid secondClaim, Guid _) = await SubmitClaimAsync(vault.SecondClient, vault.VaultId);
        await ReviewClaimAsync(recordId, secondClaim, "ACCEPT", null);

        // Hành động
        await ReviewClaimAsync(recordId, firstClaim, "REJECT", "Giấy tờ không đúng");
        HandoverRecordStatus afterReject = await ReadRecordStatusAsync(recordId);
        (Guid againClaim, Guid againRecord) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);

        // Kiểm tra
        Assert.Equal(HandoverRecordStatus.Drafting, afterReject);
        Assert.Equal(firstClaim, againClaim);
        Assert.Equal(recordId, againRecord);
    }

    [SqlFact]
    public async Task RejectedRequest_CannotBeReviewedAgainUntilItIsResubmitted()
    {
        // Chuẩn bị: một yêu cầu bị từ chối, yêu cầu còn lại giữ biên bản mở
        RejectionVault vault = await BuildVaultAsync();
        (Guid claimId, Guid recordId) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        await SubmitClaimAsync(vault.SecondClient, vault.VaultId);
        await ReviewClaimAsync(recordId, claimId, "REJECT", "Giấy tờ không đúng");
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);

        // Hành động
        HttpResponseMessage accept = await verifier.Client.PostAsJsonAsync(
            HandoverRecordTestData.VerifierRecordUrl(recordId) + "/claims/" + claimId + "/review", new { decision = "ACCEPT" });

        // Kiểm tra: bị chặn; gửi lại thì xét được
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(accept, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("ALREADY_REVIEWED", problem.GetProperty("reason").GetString());
        await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        await ReviewClaimAsync(recordId, claimId, "ACCEPT", null);
    }

    [SqlFact]
    public async Task UnlockedRecord_ClosesOnlyWhenBothPathsHaveNothingWaiting_ClaimRejectedFirst()
    {
        // Chuẩn bị: một yêu cầu và một hồ sơ báo tin cùng chờ
        RejectionVault vault = await BuildVaultAsync();
        (Guid claimId, Guid recordId) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        HttpClient relative = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid reportId = await SubmitReportAsync(relative, vault.OwnerCitizenId);

        // Hành động
        await ReviewClaimAsync(recordId, claimId, "REJECT", "Giấy tờ không đúng");
        HandoverRecordStatus afterClaim = await ReadRecordStatusAsync(recordId);
        await ReviewReportAsync(reportId, "REJECT", "Chứng tử không hợp lệ");

        // Kiểm tra
        Assert.Equal(HandoverRecordStatus.Drafting, afterClaim);
        Assert.Equal(HandoverRecordStatus.Cancelled, await ReadRecordStatusAsync(recordId));
    }

    [SqlFact]
    public async Task UnlockedRecord_ClosesOnlyWhenBothPathsHaveNothingWaiting_ReportRejectedFirst()
    {
        // Chuẩn bị
        RejectionVault vault = await BuildVaultAsync();
        (Guid claimId, Guid recordId) = await SubmitClaimAsync(vault.FirstClient, vault.VaultId);
        HttpClient relative = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid reportId = await SubmitReportAsync(relative, vault.OwnerCitizenId);

        // Hành động
        await ReviewReportAsync(reportId, "REJECT", "Chứng tử không hợp lệ");
        HandoverRecordStatus afterReport = await ReadRecordStatusAsync(recordId);
        await ReviewClaimAsync(recordId, claimId, "REJECT", "Giấy tờ không đúng");

        // Kiểm tra
        Assert.Equal(HandoverRecordStatus.Drafting, afterReport);
        Assert.Equal(HandoverRecordStatus.Cancelled, await ReadRecordStatusAsync(recordId));
    }
}
