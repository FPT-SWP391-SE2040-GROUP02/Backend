using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Handover;
using LegacyVault.Infrastructure.Notifications;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Thư mời riêng cho người nhận do luật sư thêm vào biên bản (Chủ tài sản không chia tài sản cho họ): hàm dựng thư chọn đúng
/// mẫu, và lúc chốt biên bản mỗi người nhận được đúng loại thư.
/// </summary>
[Collection("RuntimeSettings")]
public sealed class HandoverInvitationAddedByVerifierTests : IAsyncLifetime
{
    private const string Link = "http://localhost:5173/receive?token=abc";

    private readonly CapturingInvitationSender invitations = new CapturingInvitationSender();
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
            services.RemoveAll<IHandoverInvitationSender>();
            services.AddSingleton<IHandoverInvitationSender>(invitations);
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

    private sealed class CapturingInvitationSender : IHandoverInvitationSender
    {
        private readonly ConcurrentQueue<HandoverInvitation> sent = new ConcurrentQueue<HandoverInvitation>();

        public Task SendAsync(HandoverInvitation invitation, CancellationToken ct)
        {
            sent.Enqueue(invitation);
            return Task.CompletedTask;
        }

        public HandoverInvitation? FindByEmail(string email)
        {
            foreach (HandoverInvitation invitation in sent)
            {
                if (invitation.ToEmail == email)
                {
                    return invitation;
                }
            }
            return null;
        }
    }

    private static HandoverInvitation Invitation(bool addedByVerifier)
    {
        HandoverInvitation invitation = new HandoverInvitation();
        invitation.FullName = "Trần Thị B";
        invitation.ToEmail = "b@example.com";
        invitation.ResponseDeadline = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        invitation.OwnerFullName = "Nguyễn Văn A";
        invitation.CaseCode = "01A16CF1";
        invitation.AddedByVerifier = addedByVerifier;
        return invitation;
    }

    [Fact]
    public void AddedByVerifier_UsesTheLawyerAddedTemplate()
    {
        // Chuẩn bị
        HandoverInvitation invitation = Invitation(true);

        // Hành động
        EmailContent content = HandoverInvitationEmail.Build(invitation, Link, null);

        // Kiểm tra: nói luật sư thêm theo di chúc hoặc pháp luật, dặn xác minh đúng email; không nói Chủ tài sản đã chọn
        Assert.Contains("Luật sư đã thêm bạn vào biên bản bàn giao kho của Nguyễn Văn A", content.TextBody);
        Assert.Contains("di chúc hoặc quy định của pháp luật", content.TextBody);
        Assert.Contains("xác minh đúng địa chỉ email", content.TextBody);
        Assert.DoesNotContain("được chọn là người nhận", content.TextBody);
        Assert.Contains(Link, content.TextBody);
    }

    [Fact]
    public void OwnerRecipient_KeepsTheNormalTemplate()
    {
        // Chuẩn bị
        HandoverInvitation invitation = Invitation(false);

        // Hành động
        EmailContent content = HandoverInvitationEmail.Build(invitation, Link, null);

        // Kiểm tra
        Assert.Contains("được chọn là người nhận", content.TextBody);
        Assert.DoesNotContain("Luật sư đã thêm bạn", content.TextBody);
    }

    [SqlFact]
    public async Task Finalize_SendsLawyerAddedInvitationToTheAddedRecipient_AndNormalOneToTheOwnersRecipient()
    {
        // Chuẩn bị: Chủ tài sản chỉ chia cho người đầu; luật sư cho người thứ hai cùng nhận một tài sản
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid asset = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Nhà");
        string suffix = vaultId.ToString("N").Substring(0, 8);
        string firstEmail = "moi-chu-" + suffix + "@example.com";
        string secondEmail = "moi-luat-su-" + suffix + "@example.com";
        JsonElement first = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000061", ApiTestHelpers.BeneficiaryName(0), firstEmail);
        JsonElement second = await ApiTestHelpers.CreateBeneficiaryAsync(
            owner, vaultId, "001090000062", ApiTestHelpers.BeneficiaryName(1), secondEmail);
        Guid firstId = first.GetProperty("id").GetGuid();
        Guid secondId = second.GetProperty("id").GetGuid();
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, asset, firstId);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        await VaultTestData.CreateExecutorClientAsync(factory, vaultId);
        await HandoverRecordTestData.CreateVerifierClientAsync(factory);
        Guid recordId = await HandoverRecordTestData.RequestAsync(factory, vaultId);
        VaultTestData.ExecutorClient verifier = await HandoverRecordTestData.AssignedVerifierClientAsync(factory, recordId);
        await HandoverRecordTestData.AcceptAllClaimsAsync(verifier.Client, recordId);
        string url = HandoverRecordTestData.VerifierRecordUrl(recordId);
        List<object> lines = new List<object>();
        lines.Add(new { beneficiaryId = firstId, assetId = asset });
        lines.Add(new { beneficiaryId = secondId, assetId = asset });
        HttpResponseMessage set = await verifier.Client.PutAsJsonAsync(url + "/lines", new { lines });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        // Hành động
        HttpResponseMessage finalized = await verifier.Client.PostAsJsonAsync(url + "/finalize", HandoverRecordTestData.FinalizeBody);

        // Kiểm tra: người Chủ tài sản chia nhận thư thường, người luật sư thêm nhận thư riêng
        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
        HandoverInvitation? toFirst = invitations.FindByEmail(firstEmail);
        HandoverInvitation? toSecond = invitations.FindByEmail(secondEmail);
        Assert.NotNull(toFirst);
        Assert.NotNull(toSecond);
        Assert.False(toFirst!.AddedByVerifier);
        Assert.True(toSecond!.AddedByVerifier);
    }
}
