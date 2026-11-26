using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Thư báo ký theo thứ tự ký phần của mỗi người nhận (luật sư → Người thực thi → người nhận), chỉ gửi sau khi đã lưu:
/// chốt biên bản và xác nhận danh tính chưa báo ai ký; luật sư ký phần X thì báo Người thực thi ký phần X (thư nêu tên X);
/// Người thực thi ký phần X thì báo X ký. Không gửi lặp; lưu lỗi thì không có thư nào.
/// </summary>
public sealed class HandoverSigningNoticeFlowTests : IAsyncLifetime
{
    private const string RecipientEmail = "bb-nhan0@example.com";

    private readonly RecordingNoticeSender notices = new RecordingNoticeSender();
    private readonly SaveGate gate = new SaveGate();
    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IHandoverRequestNoticeSender>();
            services.AddSingleton<IHandoverRequestNoticeSender>(notices);
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(provider => new GatedUnitOfWork(provider.GetRequiredService<AppDbContext>(), gate));
        };
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

    private sealed class Prepared
    {
        public HttpClient Recipient { get; set; } = null!;
        public Guid TurnId { get; set; }
        public Guid VaultId { get; set; }
        public Guid BeneficiaryId { get; set; }
        public HandoverRecordTestData.FinalizedRecord Record { get; set; } = null!;
    }

    /// <summary>Biên bản đã chốt (đang giao) và người nhận đã đăng nhập, chưa nhận lượt.</summary>
    private async Task<Prepared> PrepareFinalizedRecordAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid asset = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000011", ApiTestHelpers.BeneficiaryName(0), RecipientEmail);
        Guid beneficiaryId = beneficiary.GetProperty("id").GetGuid();
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, asset, beneficiaryId);

        HandoverRecordTestData.FinalizedRecord record = await HandoverRecordTestData.FinalizeFromOwnerSplitAsync(factory, vaultId);
        Assert.Equal("SIGNING", record.Body.GetProperty("status").GetString());
        // Chốt xong: chưa báo ai ký (luật sư phải ký phần của từng người nhận trước).
        Assert.Empty(notices.Signing());

        HttpClient recipient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, RecipientEmail);
        JsonElement invitations = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync("/api/v1/handover/invitations/mine"));

        Prepared result = new Prepared();
        result.Recipient = recipient;
        result.TurnId = invitations[0].GetProperty("turnId").GetGuid();
        result.VaultId = vaultId;
        result.BeneficiaryId = beneficiaryId;
        result.Record = record;
        return result;
    }

    private static async Task<HttpResponseMessage> VerifierSignsAsync(Prepared prepared)
    {
        string url = HandoverRecordTestData.SignUrl(
            HandoverRecordTestData.VerifierRecordUrl(prepared.Record.RecordId), prepared.BeneficiaryId);
        return await prepared.Record.Verifier.Client.PostAsync(url, null);
    }

    private static async Task<HttpResponseMessage> ExecutorSignsAsync(Prepared prepared)
    {
        string url = HandoverRecordTestData.SignUrl(
            HandoverRecordTestData.ExecutorRecordUrl(prepared.VaultId), prepared.BeneficiaryId);
        return await prepared.Record.Executor.Client.PostAsync(url, null);
    }

    [SqlFact]
    public async Task SigningNotices_FollowTheSignOrder_RightPersonAtTheRightTime()
    {
        // Chuẩn bị
        Prepared prepared = await PrepareFinalizedRecordAsync();

        // Hành động 1: người nhận nhận lượt (môi trường test bỏ qua xác minh nên danh tính xong ngay)
        Assert.Equal(HttpStatusCode.OK,
            (await prepared.Recipient.PostAsync($"/api/v1/handover/turns/{prepared.TurnId}/claim", null)).StatusCode);

        // Kiểm tra 1: danh tính xong nhưng Người thực thi chưa ký phần của người này nên chưa báo người này ký
        Assert.Empty(notices.Signing());

        // Hành động 2: luật sư ký phần của người nhận (bấm hai lần)
        Assert.Equal(HttpStatusCode.OK, (await VerifierSignsAsync(prepared)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VerifierSignsAsync(prepared)).StatusCode);

        // Kiểm tra 2: đúng một thư báo Người thực thi, nêu tên người nhận có phần cần ký
        List<HandoverRequestNotice> afterVerifier = notices.Signing();
        Assert.Single(afterVerifier);
        Assert.Equal(HandoverRequestNotice.Executor, afterVerifier[0].Audience);
        Assert.Equal(ApiTestHelpers.BeneficiaryName(0), afterVerifier[0].RequesterName);

        // Hành động 3: Người thực thi ký phần đó
        Assert.Equal(HttpStatusCode.OK, (await ExecutorSignsAsync(prepared)).StatusCode);

        // Kiểm tra 3: thêm đúng một thư báo người nhận ký
        List<HandoverRequestNotice> afterExecutor = notices.Signing();
        Assert.Equal(2, afterExecutor.Count);
        HandoverRequestNotice toRecipient = afterExecutor.Single(n => n.Audience == HandoverRequestNotice.Recipient);
        Assert.Equal(RecipientEmail, toRecipient.ToEmail);

        // Hành động 4: người nhận đọc lại và ký
        await prepared.Recipient.GetAsync($"/api/v1/handover/turns/{prepared.TurnId}/record");
        await HandoverRecordTestData.SignAsRecipientAsync(prepared.Recipient, prepared.TurnId);

        // Kiểm tra 4: không còn thư báo ký nào thêm
        Assert.Equal(2, notices.Signing().Count);
    }

    [SqlFact]
    public async Task SigningNotice_IsNotSentWhenTheSaveFails_AndIsSentOnceOnRetry()
    {
        // Chuẩn bị: người nhận đã xong danh tính
        Prepared prepared = await PrepareFinalizedRecordAsync();
        Assert.Equal(HttpStatusCode.OK,
            (await prepared.Recipient.PostAsync($"/api/v1/handover/turns/{prepared.TurnId}/claim", null)).StatusCode);

        // Hành động 1: luật sư ký nhưng lưu lỗi giữa chừng
        gate.FailNextSave = true;
        HttpResponseMessage failed = await VerifierSignsAsync(prepared);

        // Kiểm tra 1: ký không thành, không có thư báo Người thực thi
        Assert.False(failed.IsSuccessStatusCode, "Lưu lỗi thì request phải lỗi.");
        Assert.Empty(notices.Signing());

        // Hành động 2: thử lại thành công
        Assert.Equal(HttpStatusCode.OK, (await VerifierSignsAsync(prepared)).StatusCode);

        // Kiểm tra 2: đúng một thư cho Người thực thi, không còn thư của lần lỗi
        Assert.Single(notices.Signing());
        Assert.Equal(HandoverRequestNotice.Executor, notices.Signing()[0].Audience);
    }

    private sealed class SaveGate
    {
        public volatile bool FailNextSave;
    }

    /// <summary>Bọc DbContext của request; khi cổng bật thì lần lưu kế tiếp ném lỗi (một lần).</summary>
    private sealed class GatedUnitOfWork(IUnitOfWork inner, SaveGate gate) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct)
        {
            if (gate.FailNextSave)
            {
                gate.FailNextSave = false;
                throw new InvalidOperationException("Lưu lỗi giả lập (test).");
            }
            return inner.SaveChangesAsync(ct);
        }

        public Task<T> ExecuteSerializableAsync<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            return inner.ExecuteSerializableAsync(operation, ct);
        }
    }

    private sealed class RecordingNoticeSender : IHandoverRequestNoticeSender
    {
        private readonly ConcurrentQueue<HandoverRequestNotice> all = new ConcurrentQueue<HandoverRequestNotice>();

        public Task SendAsync(HandoverRequestNotice notice, CancellationToken ct)
        {
            all.Enqueue(notice);
            return Task.CompletedTask;
        }

        public List<HandoverRequestNotice> Signing()
        {
            return all.Where(n => n.Stage == HandoverRequestNotice.StageSigning).ToList();
        }
    }
}
