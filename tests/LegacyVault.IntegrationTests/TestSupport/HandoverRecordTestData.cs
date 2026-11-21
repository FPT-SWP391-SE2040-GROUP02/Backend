using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.TestSupport;

/// <summary>
/// Đưa một kho đi qua luồng biên bản bàn giao bằng API thật: Người thực thi mở bàn giao, luật sư lấy file phân chia
/// của Chủ kho làm biên bản rồi chốt. Dùng chung cho các test cần kho đã ở giai đoạn bàn giao.
/// </summary>
public static class HandoverRecordTestData
{
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

    /// <summary>Người thực thi mở bàn giao; trả biên bản vừa tạo.</summary>
    public static async Task<JsonElement> OpenAsync(HttpClient executor, Guid vaultId)
    {
        HttpResponseMessage response = await executor.PostAsync(ExecutorRecordUrl(vaultId), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    /// <summary>Luật sư lấy file phân chia của Chủ kho làm biên bản rồi chốt; trả biên bản sau khi chốt.</summary>
    public static async Task<JsonElement> UseOwnerSplitAndFinalizeAsync(HttpClient verifier, Guid recordId)
    {
        HttpResponseMessage split = await verifier.PostAsync(VerifierRecordUrl(recordId) + "/use-owner-split", null);
        Assert.Equal(HttpStatusCode.OK, split.StatusCode);
        HttpResponseMessage finalized = await verifier.PostAsync(VerifierRecordUrl(recordId) + "/finalize", null);
        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(finalized);
    }

    /// <summary>
    /// Kho đã có gói sẵn sàng và người nhận: đặt kế hoạch Đã kích hoạt, Người thực thi mở bàn giao, luật sư chốt biên bản.
    /// Trả client của Người thực thi, của luật sư và id biên bản.
    /// </summary>
    public static async Task<FinalizedRecord> FinalizeFromOwnerSplitAsync(ApiFactory factory, Guid vaultId)
    {
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vaultId);
        await CreateVerifierClientAsync(factory);
        JsonElement opened = await OpenAsync(executor.Client, vaultId);
        Guid recordId = opened.GetProperty("id").GetGuid();
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
