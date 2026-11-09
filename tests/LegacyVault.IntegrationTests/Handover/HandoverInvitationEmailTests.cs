using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Handover;
using LegacyVault.Infrastructure.Notifications;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Nội dung thư mời nhận tài sản (thư thứ hai, sau khi Chủ kho mất). Hàm thuần, không cần SQL.</summary>
public sealed class HandoverInvitationEmailTests
{
    private const string Link = "http://localhost:5173/receive?token=abc";

    private static readonly DateTimeOffset Deadline = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static HandoverInvitation Full()
    {
        HandoverInvitation invitation = new HandoverInvitation();
        invitation.FullName = "Trần Thị B";
        invitation.ToEmail = "b@example.com";
        invitation.ResponseDeadline = Deadline;
        invitation.OwnerFullName = "Nguyễn Văn A";
        invitation.ExecutorFullName = "Lê Văn C";
        invitation.ExecutorPhone = "0901 234 567";
        invitation.ExecutorEmail = "c.le@example.com";
        return invitation;
    }

    [Fact]
    public void Email_NamesOwnerAndExecutorWithContact_AndRemindsOfTheEarlierNotice()
    {
        EmailContent content = HandoverInvitationEmail.Build(Full(), Link, null);

        Assert.Contains("Trước đây Nguyễn Văn A đã báo cho bạn biết", content.TextBody);
        Assert.Contains("Lê Văn C, người thực thi di chúc của Nguyễn Văn A", content.TextBody);
        Assert.Contains("0901 234 567", content.TextBody);
        Assert.Contains("c.le@example.com", content.TextBody);
        Assert.Contains("Nhắn Lê Văn C xin khóa mở gói", content.TextBody.Replace("3. ", string.Empty));
        Assert.Contains("16:00 ngày 12/11/2026", content.TextBody);
        Assert.Contains("[Cần xử lý trước 12/11]", content.Subject);
        Assert.Contains("Trân trọng, Đội ngũ LegacyVault", content.TextBody);
    }

    [Fact]
    public void Email_GivesFourNumberedSteps_AndNoGenderedAddress()
    {
        EmailContent content = HandoverInvitationEmail.Build(Full(), Link, null);

        Assert.Contains("1. Bấm nút", content.TextBody);
        Assert.Contains("4. Nhập khóa", content.TextBody);
        Assert.DoesNotContain(" chị ", content.TextBody);
        Assert.DoesNotContain(" ông ", content.TextBody);
    }

    [Fact]
    public void Email_WithOwnerMessage_QuotesItBeforeTheSteps()
    {
        HandoverInvitation invitation = Full();
        invitation.OwnerMessage = "Con ơi, bố để lại phần này cho con.";

        EmailContent content = HandoverInvitationEmail.Build(invitation, Link, null);

        int message = content.TextBody.IndexOf("“Con ơi, bố để lại phần này cho con.”", StringComparison.Ordinal);
        int steps = content.TextBody.IndexOf("Bạn chỉ cần làm theo bốn bước sau", StringComparison.Ordinal);
        Assert.True(message > 0);
        Assert.True(message < steps);
    }

    [Fact]
    public void Email_WithoutOwnerMessage_HasNoQuoteBlock()
    {
        EmailContent content = HandoverInvitationEmail.Build(Full(), Link, null);

        Assert.DoesNotContain("để lại cho bạn:", content.TextBody);
    }

    [Fact]
    public void Email_WithoutContacts_FallsBackToGenericWording()
    {
        HandoverInvitation invitation = new HandoverInvitation();
        invitation.FullName = "Trần Thị B";
        invitation.ResponseDeadline = Deadline;

        EmailContent content = HandoverInvitationEmail.Build(invitation, Link, null);

        Assert.Contains("Trước đây chủ kho đã báo cho bạn biết", content.TextBody);
        Assert.Contains("Nếu có thắc mắc, bạn cứ hỏi Người thực thi do chủ kho chỉ định.", content.TextBody);
    }

    [Fact]
    public void Email_ShowsSupportContactInTheWarningWhenConfigured()
    {
        EmailContent content = HandoverInvitationEmail.Build(Full(), Link, "email ho-tro@example.com");

        Assert.Contains("báo ngay cho bên mình qua email ho-tro@example.com", content.TextBody);
    }

    [Fact]
    public void Email_HtmlEncodesOwnerExecutorAndMessage()
    {
        HandoverInvitation invitation = Full();
        invitation.OwnerFullName = "<i>A</i>";
        invitation.ExecutorFullName = "<b>C</b>";
        invitation.OwnerMessage = "<script>x</script>";

        EmailContent content = HandoverInvitationEmail.Build(invitation, Link, null);

        Assert.DoesNotContain("<script>", content.HtmlBody);
        Assert.DoesNotContain("<i>A</i>", content.HtmlBody);
        Assert.DoesNotContain("<b>C</b>", content.HtmlBody);
        Assert.Contains("&lt;script&gt;", content.HtmlBody);
    }
}
