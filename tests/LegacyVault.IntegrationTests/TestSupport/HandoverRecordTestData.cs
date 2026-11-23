using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.TestSupport;

/// <summary>
/// Đưa một kho đi qua luồng biên bản bàn giao bằng API thật: người nhận gửi yêu cầu kèm giấy tờ, luật sư chấp nhận
/// giấy tờ, lấy biên bản phân chia của Chủ kho làm biên bản rồi chốt. Dùng chung cho các test cần kho đã ở giai đoạn bàn giao.
/// </summary>
public static class HandoverRecordTestData
{
    /// <summary>Body chốt biên bản: luật sư tick xác nhận căn cứ pháp lý.</summary>
    public static readonly object FinalizeBody = new { confirmLegalBasis = true };

    public static string ExecutorRecordUrl(Guid vaultId)
    {
        return $"/api/v1/executor/vaults/{vaultId}/handover-record";
    }

    public static string VerifierRecordUrl(Guid recordId)
    {
        return $"/api/v1/verifier/handover-records/{recordId}";
    }

    /// <summary>Tài khoản luật sư đang hoạt động kèm client đã gắn token.</summary>
    public static async Task<VaultTestData.ExecutorClient> CreateVerifierClientAsync(ApiFactory factory)
    {
        User user = await VaultTestData.AddVerifierAsync(factory);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        VaultTestData.ExecutorClient result = new VaultTestData.ExecutorClient();
        result.User = user;
        result.Client = client;
        return result;
    }

    /// <summary>
    /// Client của luật sư được hệ thống phân công cho biên bản. Database tạm dùng chung trong một lớp test nên có thể có
    /// nhiều luật sư; luôn đọc người được giao thật thay vì đoán.
    /// </summary>
    public static async Task<VaultTestData.ExecutorClient> AssignedVerifierClientAsync(ApiFactory factory, Guid recordId)
    {
        User? user = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Guid verifierId = await db.Set<HandoverRecord>().Where(r => r.Id == recordId).Select(r => r.VerifierUserId).SingleAsync();
            user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == verifierId);
        });
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user!);
        VaultTestData.ExecutorClient result = new VaultTestData.ExecutorClient();
        result.User = user!;
        result.Client = client;
        return result;
    }

    /// <summary>Một tệp PDF nhỏ hợp lệ theo chữ ký đầu tệp, dùng làm giấy tờ người nhận nộp.</summary>
    public static byte[] SamplePdf()
    {
        return System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% giay to thu\n%%EOF\n");
    }

    /// <summary>Multipart gửi yêu cầu nhận: token, lời nhắn và một tệp PDF.</summary>
    public static MultipartFormDataContent RequestForm(string token, string note)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "token");
        form.Add(new StringContent(note), "note");
        ByteArrayContent file = new ByteArrayContent(SamplePdf());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "files", "giay-to.pdf");
        return form;
    }

    /// <summary>Multipart gửi yêu cầu nhận trong app (không cần link): id kho, lời nhắn và một tệp PDF.</summary>
    public static MultipartFormDataContent ByAccountForm(Guid vaultId, string note)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new StringContent(vaultId.ToString()), "vaultId");
        form.Add(new StringContent(note), "note");
        ByteArrayContent file = new ByteArrayContent(SamplePdf());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "files", "giay-to.pdf");
        return form;
    }

    /// <summary>
    /// Cấp link riêng cho người nhận đầu tiên có email của kho (như lúc kích hoạt kế hoạch). Trả token và email người nhận.
    /// </summary>
    public static async Task<(string Token, string Email, Guid BeneficiaryId)> IssueRequestTokenAsync(ApiFactory factory, Guid vaultId)
    {
        (string value, string hash) = LegacyVault.Application.Services.HandoverRecordRules.CreateRequestToken();
        string email = string.Empty;
        Guid beneficiaryId = Guid.Empty;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            LegacyVault.Infrastructure.Data.AppDbContext db =
                scope.ServiceProvider.GetRequiredService<LegacyVault.Infrastructure.Data.AppDbContext>();
            LegacyVault.Application.Security.IBeneficiaryDataProtector protector =
                scope.ServiceProvider.GetRequiredService<LegacyVault.Application.Security.IBeneficiaryDataProtector>();
            List<Beneficiary> beneficiaries = await db.Set<Beneficiary>()
                .Where(b => b.VaultId == vaultId).OrderBy(b => b.CreatedAt).ToListAsync();
            foreach (Beneficiary beneficiary in beneficiaries)
            {
                string? candidate = protector.Unprotect(beneficiary).Email;
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    beneficiary.RequestTokenHash = hash;
                    email = candidate.Trim().ToLowerInvariant();
                    beneficiaryId = beneficiary.Id;
                    break;
                }
            }
            Assert.NotEqual(Guid.Empty, beneficiaryId);
            await db.SaveChangesAsync();
        }
        return (value, email, beneficiaryId);
    }

    /// <summary>
    /// Người nhận đầu tiên của kho gửi yêu cầu nhận tài sản kèm một tệp PDF; trả id biên bản vừa tạo hoặc đang mở.
    /// </summary>
    public static async Task<Guid> RequestAsync(ApiFactory factory, Guid vaultId)
    {
        (string token, string email, Guid _) = await IssueRequestTokenAsync(factory, vaultId);
        HttpClient recipient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, email);
        HttpResponseMessage response = await recipient.PostAsync("/api/v1/handover/requests", RequestForm(token, "Xin nhận tài sản"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement claim = await ApiTestHelpers.ReadJsonAsync(response);
        return claim.GetProperty("recordId").GetGuid();
    }

    /// <summary>Luật sư chấp nhận giấy tờ của mọi yêu cầu đang chờ xét.</summary>
    public static async Task AcceptAllClaimsAsync(HttpClient verifier, Guid recordId)
    {
        JsonElement record = await ApiTestHelpers.ReadJsonAsync(await verifier.GetAsync(VerifierRecordUrl(recordId)));
        foreach (JsonElement claim in record.GetProperty("claims").EnumerateArray())
        {
            if (claim.GetProperty("status").GetString() != "SUBMITTED")
            {
                continue;
            }
            Guid claimId = claim.GetProperty("id").GetGuid();
            HttpResponseMessage reviewed = await verifier.PostAsJsonAsync(
                VerifierRecordUrl(recordId) + "/claims/" + claimId + "/review", new { decision = "ACCEPT" });
            Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        }
    }

    /// <summary>Luật sư lấy biên bản phân chia của Chủ kho làm biên bản rồi chốt; trả biên bản sau khi chốt.</summary>
    public static async Task<JsonElement> UseOwnerSplitAndFinalizeAsync(HttpClient verifier, Guid recordId)
    {
        await AcceptAllClaimsAsync(verifier, recordId);
        HttpResponseMessage split = await verifier.PostAsync(VerifierRecordUrl(recordId) + "/use-owner-split", null);
        Assert.Equal(HttpStatusCode.OK, split.StatusCode);
        HttpResponseMessage finalized = await verifier.PostAsJsonAsync(VerifierRecordUrl(recordId) + "/finalize", FinalizeBody);
        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(finalized);
    }

    /// <summary>
    /// Kho đã có gói sẵn sàng và người nhận: đặt kế hoạch Đã kích hoạt, người nhận gửi yêu cầu, luật sư chốt biên bản.
    /// Trả client của Người thực thi, của luật sư và id biên bản.
    /// </summary>
    public static async Task<FinalizedRecord> FinalizeFromOwnerSplitAsync(ApiFactory factory, Guid vaultId)
    {
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vaultId);
        await CreateVerifierClientAsync(factory);
        Guid recordId = await RequestAsync(factory, vaultId);
        VaultTestData.ExecutorClient verifier = await AssignedVerifierClientAsync(factory, recordId);
        JsonElement finalized = await UseOwnerSplitAndFinalizeAsync(verifier.Client, recordId);

        FinalizedRecord result = new FinalizedRecord();
        result.Executor = executor;
        result.Verifier = verifier;
        result.RecordId = recordId;
        result.Body = finalized;
        return result;
    }

    /// <summary>Luật sư và Người thực thi ký biên bản (bước 3). Biên bản phải đang ở bước ký.</summary>
    public static async Task SignAsVerifierAndExecutorAsync(FinalizedRecord record, Guid vaultId)
    {
        HttpResponseMessage verifier = await record.Verifier.Client.PostAsync(VerifierRecordUrl(record.RecordId) + "/sign", null);
        Assert.Equal(HttpStatusCode.OK, verifier.StatusCode);
        HttpResponseMessage executor = await record.Executor.Client.PostAsync(ExecutorRecordUrl(vaultId) + "/sign", null);
        Assert.Equal(HttpStatusCode.OK, executor.StatusCode);
    }

    /// <summary>Người nhận ký biên bản từ lượt của mình; trả biên bản sau khi ký.</summary>
    public static async Task<JsonElement> SignAsRecipientAsync(HttpClient recipient, Guid turnId)
    {
        HttpResponseMessage response = await recipient.PostAsync($"/api/v1/handover/turns/{turnId}/record/sign", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    public sealed class FinalizedRecord
    {
        public VaultTestData.ExecutorClient Executor { get; set; } = null!;
        public VaultTestData.ExecutorClient Verifier { get; set; } = null!;
        public Guid RecordId { get; set; }
        public JsonElement Body { get; set; }
    }
}
