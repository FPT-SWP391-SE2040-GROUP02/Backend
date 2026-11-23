using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Handover;
using LegacyVault.Infrastructure.Notifications;
using Xunit;

namespace LegacyVault.IntegrationTests.Email;

/// <summary>Thư báo biên bản đã tới bước ký (người nhận và Người thực thi). Hàm thuần, không cần SQL.</summary>
public sealed class HandoverSigningNoticeEmailTests
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
            Stage = HandoverRequestNotice.StageSigning
        };
    }

    [Fact]
    public void RecipientSigningNotice_PointsToRecipientPortalAndAsksToSign()
    {
        EmailContent content = HandoverRequestNoticeEmail.Build(Notice(HandoverRequestNotice.Recipient), ClientUrl);

        Assert.Equal(HandoverRequestNoticeEmail.SigningSubject, content.Subject);
        Assert.Contains("https://app.legacyvault.test/claim/beneficiary", content.TextBody);
        Assert.Contains("ký", content.TextBody);
        Assert.Contains("Nguyễn Thanh Duy", content.TextBody);
    }

    [Fact]
    public void ExecutorSigningNotice_PointsToExecutorWorkspace()
    {
        EmailContent content = HandoverRequestNoticeEmail.Build(Notice(HandoverRequestNotice.Executor), ClientUrl);

        Assert.Equal(HandoverRequestNoticeEmail.SigningSubject, content.Subject);
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
