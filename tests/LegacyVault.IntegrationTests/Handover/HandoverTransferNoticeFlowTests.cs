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
/// Thư báo "luật sư đã ký biên bản" chỉ gửi sau khi đã lưu: chưa ký thì không có thư,
/// ký xong thì gửi đúng một thư cho người nhận và một cho Người thực thi, không gửi lặp;
/// lưu lỗi thì không có thư nào.
/// </summary>
public sealed class HandoverTransferNoticeFlowTests : IAsyncLifetime
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

    /// <summary>Biên bản đã chốt (bước xác minh danh tính) và người nhận đã đăng nhập, chưa nhận lượt.</summary>
    private async Task<(HttpClient Recipient, Guid TurnId, HttpClient Verifier, string SignUrl)> PrepareFinalizedRecordAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid asset = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói M")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, asset)).StatusCode);
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000011", ApiTestHelpers.BeneficiaryName(0), RecipientEmail);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            owner, vaultId, containerId, beneficiary.GetProperty("id").GetGuid())).StatusCode);

        HandoverRecordTestData.FinalizedRecord record = await HandoverRecordTestData.FinalizeFromOwnerSplitAsync(factory, vaultId);
        Assert.Equal("IDENTITY_CHECK", record.Body.GetProperty("status").GetString());
        Assert.Empty(notices.Transfers());

        HttpClient recipient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, RecipientEmail);
        JsonElement invitations = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync("/api/v1/handover/invitations/mine"));
        return (recipient, invitations[0].GetProperty("turnId").GetGuid(), record.Verifier.Client,
            HandoverRecordTestData.VerifierRecordUrl(record.RecordId) + "/sign");
    }

    [SqlFact]
    public async Task TransferNotice_IsSentOnceAfterVerifierSigns()
    {
        (HttpClient recipient, Guid turnId, HttpClient verifier, string signUrl) = await PrepareFinalizedRecordAsync();

        // Môi trường test bỏ qua xác minh danh tính nên nhận lượt là biên bản sang bước ký.
        Assert.Equal(HttpStatusCode.OK, (await recipient.PostAsync($"/api/v1/handover/turns/{turnId}/claim", null)).StatusCode);
        JsonElement view = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync($"/api/v1/handover/turns/{turnId}/record"));
        Assert.Equal("SIGNING", view.GetProperty("status").GetString());
        Assert.Empty(notices.Transfers());
        Assert.Equal(HttpStatusCode.OK, (await verifier.PostAsync(signUrl, null)).StatusCode);

        List<HandoverRequestNotice> sent = notices.Transfers();
        Assert.Equal(2, sent.Count);
        Assert.Equal(1, sent.Count(n => n.Audience == HandoverRequestNotice.Recipient));
        Assert.Equal(1, sent.Count(n => n.Audience == HandoverRequestNotice.Executor));

        // Đọc lại biên bản không gửi thêm thư.
        await recipient.GetAsync($"/api/v1/handover/turns/{turnId}/record");
        Assert.Equal(2, notices.Transfers().Count);
    }

    [SqlFact]
    public async Task TransferNotice_IsNotSentWhenTheSaveFails_AndIsSentOnceOnRetry()
    {
        (HttpClient recipient, Guid turnId, HttpClient verifier, string signUrl) = await PrepareFinalizedRecordAsync();

        Assert.Equal(HttpStatusCode.OK, (await recipient.PostAsync($"/api/v1/handover/turns/{turnId}/claim", null)).StatusCode);
        // Lưu lỗi giữa chừng: ký không thành, không có thư báo nào được gửi.
        gate.FailNextSave = true;
        HttpResponseMessage failed = await verifier.PostAsync(signUrl, null);
        Assert.False(failed.IsSuccessStatusCode, "Lưu lỗi thì request phải lỗi.");
        Assert.Empty(notices.Transfers());

        // Thử lại thành công: đúng một thư cho mỗi người, không còn thư của lần lỗi.
        Assert.Equal(HttpStatusCode.OK, (await verifier.PostAsync(signUrl, null)).StatusCode);
        Assert.Equal(2, notices.Transfers().Count);
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

        public List<HandoverRequestNotice> Transfers()
        {
            return all.Where(n => n.Stage == HandoverRequestNotice.StageTransfer).ToList();
        }
    }
}
