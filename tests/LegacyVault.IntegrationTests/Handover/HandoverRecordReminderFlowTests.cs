using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.Audit;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Luật sư nhắc Người thực thi ký (SQL Server thật): nhắc được một lần mỗi 24 giờ, thư gửi đúng người, có nhật ký; và luật sư
/// được báo một lần khi chỉ còn thiếu chữ ký của mình.
/// </summary>
public sealed class HandoverRecordReminderFlowTests : IAsyncLifetime
{
    private const string RecipientEmail = "nhac-nhan0@example.com";

    private readonly RecordingNoticeSender notices = new RecordingNoticeSender();
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

    private sealed class RecordingNoticeSender : IHandoverRequestNoticeSender
    {
        private readonly ConcurrentQueue<HandoverRequestNotice> all = new ConcurrentQueue<HandoverRequestNotice>();

        public Task SendAsync(HandoverRequestNotice notice, CancellationToken ct)
        {
            all.Enqueue(notice);
            return Task.CompletedTask;
        }

        public List<HandoverRequestNotice> OfStage(string stage)
        {
            List<HandoverRequestNotice> result = new List<HandoverRequestNotice>();
            foreach (HandoverRequestNotice notice in all)
            {
                if (notice.Stage == stage)
                {
                    result.Add(notice);
                }
            }
            return result;
        }
    }

    /// <summary>Kho có một người nhận được gán một tài sản; luật sư đã chốt biên bản.</summary>
    private async Task<(Guid VaultId, HandoverRecordTestData.FinalizedRecord Record)> BuildFinalizedAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid asset = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000051", ApiTestHelpers.BeneficiaryName(0), RecipientEmail);
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, asset, beneficiary.GetProperty("id").GetGuid());
        HandoverRecordTestData.FinalizedRecord record = await HandoverRecordTestData.FinalizeFromOwnerSplitAsync(factory, vaultId);
        return (vaultId, record);
    }

    private static string RemindUrl(Guid recordId)
    {
        return HandoverRecordTestData.VerifierRecordUrl(recordId) + "/remind-executor";
    }

    /// <summary>Danh tính người nhận xong và luật sư đã ký phần của họ: có phần chờ Người thực thi ký.</summary>
    private async Task VerifierSignsEveryPartAsync(HandoverRecordTestData.FinalizedRecord record)
    {
        await HandoverRecordTestData.ConfirmIdentitiesAsync(factory, record.RecordId);
        await HandoverRecordTestData.SignAsVerifierAsync(factory, record);
    }

    [SqlFact]
    public async Task RemindBeforeVerifierSignedAnyPart_Returns409_VerifierNotSigned()
    {
        // Chuẩn bị: biên bản đã chốt, danh tính xong, luật sư chưa ký phần nào
        (Guid _, HandoverRecordTestData.FinalizedRecord record) = await BuildFinalizedAsync();
        await HandoverRecordTestData.ConfirmIdentitiesAsync(factory, record.RecordId);

        // Hành động
        HttpResponseMessage response = await record.Verifier.Client.PostAsync(RemindUrl(record.RecordId), null);

        // Kiểm tra: chưa có phần nào để Người thực thi ký nên không nhắc, không gửi thư
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("VERIFIER_NOT_SIGNED", problem.GetProperty("reason").GetString());
        Assert.Empty(notices.OfStage(HandoverRequestNotice.StageExecutorReminder));
    }

    [SqlFact]
    public async Task Remind_Works_SetsReminderTime_SendsMailToExecutor_AndWritesAudit()
    {
        // Chuẩn bị: luật sư đã ký phần của người nhận, Người thực thi chưa ký
        (Guid vaultId, HandoverRecordTestData.FinalizedRecord record) = await BuildFinalizedAsync();
        await VerifierSignsEveryPartAsync(record);

        // Hành động
        HttpResponseMessage response = await record.Verifier.Client.PostAsync(RemindUrl(record.RecordId), null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.String, body.GetProperty("executorReminderSentAt").ValueKind);
        DateTimeOffset sentAt = body.GetProperty("executorReminderSentAt").GetDateTimeOffset();
        DateTimeOffset availableAt = body.GetProperty("executorRemindAvailableAt").GetDateTimeOffset();
        Assert.Equal(sentAt.AddHours(24), availableAt);
        List<HandoverRequestNotice> reminders = notices.OfStage(HandoverRequestNotice.StageExecutorReminder);
        Assert.Single(reminders);
        Assert.Equal(HandoverRequestNotice.Executor, reminders[0].Audience);
        Assert.Equal(record.Executor.User.Email, reminders[0].ToEmail);
        List<AuditLog> logs = await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.HandoverExecutorReminded);
        Assert.Single(logs);
        Assert.Equal(record.Verifier.User.Id, logs[0].ActorUserId);
    }

    [SqlFact]
    public async Task RemindAgainWithin24Hours_Returns409_WithRemindAvailableAt_AndSendsNoSecondMail()
    {
        // Chuẩn bị
        (Guid _, HandoverRecordTestData.FinalizedRecord record) = await BuildFinalizedAsync();
        await VerifierSignsEveryPartAsync(record);
        Assert.Equal(HttpStatusCode.OK, (await record.Verifier.Client.PostAsync(RemindUrl(record.RecordId), null)).StatusCode);

        // Hành động
        HttpResponseMessage again = await record.Verifier.Client.PostAsync(RemindUrl(record.RecordId), null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        JsonElement problem = await ApiTestHelpers.ReadJsonAsync(again);
        Assert.Equal("HANDOVER_NOT_ALLOWED", problem.GetProperty("code").GetString());
        Assert.Equal("REMINDED_RECENTLY", problem.GetProperty("reason").GetString());
        DateTimeOffset availableAt = problem.GetProperty("remindAvailableAt").GetDateTimeOffset();
        Assert.True(availableAt > DateTimeOffset.UtcNow.AddHours(23));
        Assert.Single(notices.OfStage(HandoverRequestNotice.StageExecutorReminder));
    }

    [SqlFact]
    public async Task RemindAfterExecutorSigned_Returns409_ExecutorAlreadySigned()
    {
        // Chuẩn bị
        (Guid vaultId, HandoverRecordTestData.FinalizedRecord record) = await BuildFinalizedAsync();
        await VerifierSignsEveryPartAsync(record);
        foreach (Guid id in await HandoverRecordTestData.RecipientIdsAsync(factory, record.RecordId))
        {
            Assert.Equal(HttpStatusCode.OK, (await record.Executor.Client.PostAsync(
                HandoverRecordTestData.SignUrl(HandoverRecordTestData.ExecutorRecordUrl(vaultId), id), null)).StatusCode);
        }

        // Hành động
        HttpResponseMessage response = await record.Verifier.Client.PostAsync(RemindUrl(record.RecordId), null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        JsonElement problem = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("EXECUTOR_ALREADY_SIGNED", problem.GetProperty("reason").GetString());
        Assert.Empty(notices.OfStage(HandoverRequestNotice.StageExecutorReminder));
    }

    [SqlFact]
    public async Task RemindByAnotherVerifier_Returns404()
    {
        // Chuẩn bị
        (Guid _, HandoverRecordTestData.FinalizedRecord record) = await BuildFinalizedAsync();
        VaultTestData.ExecutorClient other = await HandoverRecordTestData.CreateVerifierClientAsync(factory);

        // Hành động
        HttpResponseMessage response = await other.Client.PostAsync(RemindUrl(record.RecordId), null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(notices.OfStage(HandoverRequestNotice.StageExecutorReminder));
    }

    [SqlFact]
    public async Task RemindBeforeFinalize_Returns409_WrongState()
    {
        // Chuẩn bị: biên bản mới có yêu cầu, luật sư chưa chốt
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid asset = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000052", ApiTestHelpers.BeneficiaryName(0), "nhac-chua-chot@example.com");
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, asset, beneficiary.GetProperty("id").GetGuid());
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        await VaultTestData.CreateExecutorClientAsync(factory, vaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        Guid recordId = await HandoverRecordTestData.RequestAsync(factory, vaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);

        // Hành động
        HttpResponseMessage response = await verifier.Client.PostAsync(RemindUrl(recordId), null);

        // Kiểm tra
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        JsonElement problem = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal("WRONG_STATE", problem.GetProperty("reason").GetString());
    }

    [SqlFact]
    public async Task VerifierIsNotifiedOnce_WhenOnlyTheirSignatureIsMissing_OnAnOldOutOfOrderRecord()
    {
        // Chuẩn bị: biên bản đã chốt, người nhận đã đăng nhập và nhận lượt; biên bản cũ đang dở có chữ ký Người thực thi
        // từ trước khi có luật thứ tự (luật sư chưa ký) — chữ ký này được giữ nguyên
        (Guid _, HandoverRecordTestData.FinalizedRecord record) = await BuildFinalizedAsync();
        HttpClient recipient = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true, RecipientEmail);
        JsonElement invitations = await ApiTestHelpers.ReadJsonAsync(await recipient.GetAsync("/api/v1/handover/invitations/mine"));
        Guid turnId = invitations[0].GetProperty("turnId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await recipient.PostAsync($"/api/v1/handover/turns/{turnId}/claim", null)).StatusCode);
        Guid beneficiaryId = (await HandoverRecordTestData.RecipientIdsAsync(factory, record.RecordId))[0];
        await HandoverRecordTestData.InsertSignatureAsync(
            factory, record.RecordId, HandoverSignerRole.Executor, record.Executor.User.Id, beneficiaryId);
        int afterExecutor = notices.OfStage(HandoverRequestNotice.StageVerifierSignNeeded).Count;

        // Hành động: người nhận ký (Người thực thi đã ký phần của mình nên ký được), cuối cùng luật sư ký
        await HandoverRecordTestData.SignAsRecipientAsync(recipient, turnId);
        List<HandoverRequestNotice> afterRecipient = notices.OfStage(HandoverRequestNotice.StageVerifierSignNeeded);
        HttpResponseMessage verifierSigned = await record.Verifier.Client.PostAsync(
            HandoverRecordTestData.SignUrl(HandoverRecordTestData.VerifierRecordUrl(record.RecordId), beneficiaryId), null);

        // Kiểm tra: chỉ một thư, tới luật sư, gửi lúc người nhận ký xong; luật sư ký xong thì biên bản hoàn tất
        Assert.Equal(0, afterExecutor);
        Assert.Equal(HttpStatusCode.OK, verifierSigned.StatusCode);
        Assert.Equal("COMPLETED", (await ApiTestHelpers.ReadJsonAsync(verifierSigned)).GetProperty("status").GetString());
        Assert.Single(afterRecipient);
        Assert.Equal(HandoverRequestNotice.Verifier, afterRecipient[0].Audience);
        Assert.Equal(record.Verifier.User.Email, afterRecipient[0].ToEmail);
        Assert.Single(notices.OfStage(HandoverRequestNotice.StageVerifierSignNeeded));
    }
}
