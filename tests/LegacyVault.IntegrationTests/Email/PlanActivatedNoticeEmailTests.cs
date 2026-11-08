using System.Net;
using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Handover;
using LegacyVault.Infrastructure.Notifications;
using Xunit;

namespace LegacyVault.IntegrationTests.Email;

/// <summary>Kiểm tra nội dung thư báo người nhận khi Chủ kho kích hoạt kế hoạch. Hàm thuần, không cần SQL hay mạng.</summary>
public sealed class PlanActivatedNoticeEmailTests
{
    private const string LandingUrl = "http://localhost:5173/";

    private static PlanActivatedNotice Notice()
    {
        return new PlanActivatedNotice
        {
            ToEmail = "nguoinhan@test.local",
            FullName = "Lê <Người> Nhận",
            OwnerName = "Nguyễn Chủ Kho",
            OwnerEmail = "chukho@test.local",
            OwnerPhone = "0912345678",
            ExecutorName = "Trần Thực Thi",
            ExecutorEmail = "thucthi@test.local",
            ExecutorPhone = "0987654321",
        };
    }

    [Fact]
    public void Build_HasTitleContactsAndLandingLink()
    {
        EmailContent email = PlanActivatedNoticeEmail.Build(Notice(), LandingUrl);

        Assert.Equal(PlanActivatedNoticeEmail.Subject, email.Subject);
        Assert.Contains(WebUtility.HtmlEncode(PlanActivatedNoticeEmail.Title), email.HtmlBody);
        Assert.Contains(PlanActivatedNoticeEmail.Title, email.TextBody);

        foreach (string value in new[] { "chukho@test.local", "0912345678", "thucthi@test.local", "0987654321" })
        {
            Assert.Contains(value, email.TextBody);
            Assert.Contains(value, email.HtmlBody);
        }

        Assert.Contains(LandingUrl, email.TextBody);
        Assert.Contains(LandingUrl, email.HtmlBody);
        Assert.Contains(LegacyVaultEmailTheme.LogoContentId, email.HtmlBody);
    }

    [Fact]
    public void Build_HasNoSmallKickerAndNoInvitationToken()
    {
        EmailContent email = PlanActivatedNoticeEmail.Build(Notice(), LandingUrl);

        Assert.DoesNotContain(WebUtility.HtmlEncode("Thông báo bàn giao"), email.HtmlBody);
        Assert.DoesNotContain("token=", email.HtmlBody);
        Assert.Contains("&lt;Người&gt;", email.HtmlBody);
        Assert.DoesNotContain("<Người>", email.HtmlBody);
    }

    [Fact]
    public void Build_MissingPhone_SaysNotProvided()
    {
        PlanActivatedNotice notice = Notice();
        notice.ExecutorPhone = null;

        EmailContent email = PlanActivatedNoticeEmail.Build(notice, LandingUrl);

        Assert.Contains("số điện thoại chưa cung cấp", email.TextBody);
    }
}
