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
/// giấy tờ, lấy biên bản phân chia của Chủ tài sản làm biên bản rồi chốt. Dùng chung cho các test cần kho đã ở giai đoạn bàn giao.
/// </summary>
public static class HandoverRecordTestData
{
    /// <summary>
    /// Body chốt biên bản: luật sư tick xác nhận căn cứ pháp lý và (khi biên bản khác cách chia của Chủ tài sản) xác nhận khác
    /// biệt; không khác thì cờ thứ hai bị bỏ qua.
    /// </summary>
    public static readonly object FinalizeBody = new { confirmLegalBasis = true, confirmSplitDeviation = true };

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

    /// <summary>Hai loại giấy tờ bắt buộc khi gửi yêu cầu nhận: một giấy chứng tử và một di chúc (PDF nhỏ).</summary>
    private static void AddRequiredDocuments(MultipartFormDataContent form)
    {
        ByteArrayContent certificate = new ByteArrayContent(SamplePdf());
        certificate.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(certificate, "deathCertificates", "chung-tu.pdf");
        ByteArrayContent will = new ByteArrayContent(SamplePdf());
        will.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(will, "wills", "giay-to.pdf");
    }

    /// <summary>Multipart gửi yêu cầu nhận: token, lời nhắn, giấy chứng tử và di chúc.</summary>
    public static MultipartFormDataContent RequestForm(string token, string note)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "token");
        form.Add(new StringContent(note), "note");
        AddRequiredDocuments(form);
        return form;
    }

    /// <summary>Multipart gửi yêu cầu nhận trong app (không cần link): id kho, lời nhắn, giấy chứng tử và di chúc.</summary>
    public static MultipartFormDataContent ByAccountForm(Guid vaultId, string note)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        form.Add(new StringContent(vaultId.ToString()), "vaultId");
        form.Add(new StringContent(note), "note");
        AddRequiredDocuments(form);
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

    /// <summary>Luật sư lấy biên bản phân chia của Chủ tài sản làm biên bản rồi chốt; trả biên bản sau khi chốt.</summary>
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

    /// <summary>Đường ký phần của một người nhận, tính từ đường biên bản của luật sư hoặc Người thực thi.</summary>
    public static string SignUrl(string recordUrl, Guid beneficiaryId)
    {
        return recordUrl + "/recipients/" + beneficiaryId + "/sign";
    }

    /// <summary>Người nhận trong biên bản; confirmedOnly thì chỉ người đã được xác nhận danh tính.</summary>
    public static async Task<List<Guid>> RecipientIdsAsync(ApiFactory factory, Guid recordId, bool confirmedOnly = false)
    {
        List<Guid> result = new List<Guid>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<Guid> ids = await db.Set<HandoverRecordLine>().Where(l => l.RecordId == recordId)
                .Select(l => l.BeneficiaryId).Distinct().ToListAsync();
            foreach (Guid id in ids)
            {
                bool ready = await db.Set<HandoverTurn>().AnyAsync(t => t.RecordId == recordId && t.BeneficiaryId == id
                    && (t.Status == HandoverTurnStatus.ReadyToAccept || t.Status == HandoverTurnStatus.HandoverCommitted));
                if (!confirmedOnly || ready)
                {
                    result.Add(id);
                }
            }
        });
        return result;
    }

    /// <summary>Đặt mọi lượt đang mời là đã xác nhận danh tính, cho những test không đi qua bước xác minh.</summary>
    public static async Task ConfirmIdentitiesAsync(ApiFactory factory, Guid recordId)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<HandoverTurn>().Where(t => t.RecordId == recordId && t.Status == HandoverTurnStatus.Invited)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, HandoverTurnStatus.ReadyToAccept));
        });
    }

    /// <summary>
    /// Luật sư rồi Người thực thi ký phần của từng người nhận đã được xác nhận danh tính (đúng thứ tự ký; bấm lại không
    /// ghi trùng).
    /// </summary>
    public static async Task SignAsVerifierAndExecutorAsync(ApiFactory factory, FinalizedRecord record, Guid vaultId)
    {
        foreach (Guid beneficiaryId in await RecipientIdsAsync(factory, record.RecordId, confirmedOnly: true))
        {
            HttpResponseMessage verifier = await record.Verifier.Client.PostAsync(
                SignUrl(VerifierRecordUrl(record.RecordId), beneficiaryId), null);
            Assert.Equal(HttpStatusCode.OK, verifier.StatusCode);
            HttpResponseMessage executor = await record.Executor.Client.PostAsync(
                SignUrl(ExecutorRecordUrl(vaultId), beneficiaryId), null);
            Assert.Equal(HttpStatusCode.OK, executor.StatusCode);
        }
    }

    /// <summary>Luật sư ký phần của từng người nhận đã được xác nhận danh tính (bước đầu của thứ tự ký).</summary>
    public static async Task SignAsVerifierAsync(ApiFactory factory, FinalizedRecord record)
    {
        foreach (Guid beneficiaryId in await RecipientIdsAsync(factory, record.RecordId, confirmedOnly: true))
        {
            HttpResponseMessage verifier = await record.Verifier.Client.PostAsync(
                SignUrl(VerifierRecordUrl(record.RecordId), beneficiaryId), null);
            Assert.Equal(HttpStatusCode.OK, verifier.StatusCode);
        }
    }

    /// <summary>
    /// Ghi thẳng vào DB một chữ ký của bản hiện tại, mô phỏng biên bản đang dở đã ký sai thứ tự từ trước khi có luật thứ tự
    /// (API không còn cho ký như vậy).
    /// </summary>
    public static async Task InsertSignatureAsync(
        ApiFactory factory, Guid recordId, HandoverSignerRole role, Guid signerUserId, Guid? beneficiaryId)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            HandoverRecord record = await db.Set<HandoverRecord>().SingleAsync(r => r.Id == recordId);
            HandoverRecordSignature signature = new HandoverRecordSignature
            {
                RecordId = recordId,
                RevisionNo = record.RevisionNo,
                Role = role,
                SignerUserId = signerUserId,
                BeneficiaryId = beneficiaryId,
                SignedAt = DateTimeOffset.UtcNow
            };
            db.Set<HandoverRecordSignature>().Add(signature);
            await db.SaveChangesAsync();
        });
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
