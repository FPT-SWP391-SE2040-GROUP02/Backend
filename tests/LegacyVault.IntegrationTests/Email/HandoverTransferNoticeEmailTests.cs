using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Handover;
using LegacyVault.Infrastructure.Notifications;
using Xunit;

namespace LegacyVault.IntegrationTests.Email;

/// <summary>Thư báo luật sư đã ký biên bản (người nhận và Người thực thi). Hàm thuần, không cần SQL.</summary>
public sealed class HandoverTransferNoticeEmailTests
{
    private const string ClientUrl = "https://app.legacyvault.test/";

    private static HandoverRequestNotice Notice(string audience)
    {
        return new HandoverRequestNotice
        {
            ToEmail = "nguoi@example.com",
            FullName = "Phan Khánh Duy",
            Audience = audience,
            OwnerName = "Nguyễn Thanh Duy",
            Stage = HandoverRequestNotice.StageTransfer
        };
    }

    [Fact]
    public void RecipientTransferNotice_PointsToRecipientPortal()
    {
        EmailContent content = HandoverRequestNoticeEmail.Build(Notice(HandoverRequestNotice.Recipient), ClientUrl);

        Assert.Equal(HandoverRequestNoticeEmail.TransferSubject, content.Subject);
        Assert.Contains("https://app.legacyvault.test/claim/beneficiary", content.TextBody);
        Assert.Contains("ký", content.TextBody);
        Assert.Contains("Nguyễn Thanh Duy", content.TextBody);
    }

    [Fact]
    public void ExecutorTransferNotice_PointsToExecutorWorkspace()
    {
        EmailContent content = HandoverRequestNoticeEmail.Build(Notice(HandoverRequestNotice.Executor), ClientUrl);

        Assert.Equal(HandoverRequestNoticeEmail.TransferSubject, content.Subject);
        Assert.Contains("https://app.legacyvault.test/executor", content.TextBody);
        Assert.Contains("mã mở gói", content.TextBody);
    }

    [Fact]
    public void RequestNotice_KeepsItsOwnSubject()
    {
        HandoverRequestNotice notice = Notice(HandoverRequestNotice.Executor);
        notice.Stage = HandoverRequestNotice.StageRequest;
        notice.RequesterName = "Phan Khánh Duy";

        EmailContent content = HandoverRequestNoticeEmail.Build(notice, ClientUrl);

        Assert.Equal(HandoverRequestNoticeEmail.Subject, content.Subject);
    }
}
