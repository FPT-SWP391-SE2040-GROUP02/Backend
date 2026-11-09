using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Handover;
using LegacyVault.Infrastructure.Notifications;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Thư mời theo prototype: mã hồ sơ, Người xét duyệt, thông tin công ty và cảnh báo lừa đảo.</summary>
[Collection("RuntimeSettings")]
public sealed class HandoverInvitationMailContentTests
{
    private const string Link = "http://localhost:5173/receive?token=abc";

    private static HandoverInvitation Invitation()
    {
        HandoverInvitation invitation = new HandoverInvitation();
        invitation.FullName = "Trần Thị B";
        invitation.ToEmail = "b@example.com";
        invitation.ResponseDeadline = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        invitation.OwnerFullName = "Nguyễn Văn A";
        invitation.ExecutorFullName = "Lê Văn C";
        invitation.CaseCode = "01A16CF1";
        invitation.VerifierFullName = "Luật sư Dũng";
        invitation.VerifierLawyerCardNumber = "LS-12345";
        return invitation;
    }

    private static void WithCompany(Action body)
    {
        RuntimeSettings.Replace(new Dictionary<string, string>
        {
            ["Company.Name"] = "Công ty Di Sản Số",
            ["Company.Phone"] = "028 1234 5678",
            ["Company.Website"] = "disan.example",
        });
        try
        {
            body();
        }
        finally
        {
            RuntimeSettings.Replace(new Dictionary<string, string>());
        }
    }

    [Fact]
    public void Mail_ShowsCaseCodeAndTellsRecipientHowToCheckTheMail()
    {
        EmailContent content = HandoverInvitationEmail.Build(Invitation(), Link, null);

        Assert.Contains("Mã hồ sơ: 01A16CF1", content.TextBody);
        Assert.Contains("4 số cuối căn cước", content.TextBody);
    }

    [Fact]
    public void Mail_NamesTheVerifierWithLawyerCard()
    {
        EmailContent content = HandoverInvitationEmail.Build(Invitation(), Link, null);

        Assert.Contains("Người xét duyệt Luật sư Dũng (thẻ luật sư LS-12345)", content.TextBody);
    }

    [Fact]
    public void Mail_WithoutCaseCodeOrVerifier_OmitsThoseLinesInsteadOfShowingEmptyOnes()
    {
        HandoverInvitation invitation = Invitation();
        invitation.CaseCode = string.Empty;
        invitation.VerifierFullName = string.Empty;

        EmailContent content = HandoverInvitationEmail.Build(invitation, Link, null);

        Assert.DoesNotContain("Mã hồ sơ:", content.TextBody);
        Assert.DoesNotContain("Hồ sơ đã được Người xét duyệt", content.TextBody);
    }

    [Fact]
    public void Mail_HasScamWarningWithCompanyContactFromAdminSettings()
    {
        WithCompany(() =>
        {
            EmailContent content = HandoverInvitationEmail.Build(Invitation(), Link, null);

            Assert.Contains("Cảnh báo lừa đảo", content.TextBody);
            Assert.Contains("không bao giờ hỏi mật khẩu", content.TextBody);
            Assert.Contains("gọi số 028 1234 5678", content.TextBody);
            Assert.Contains("Công ty Di Sản Số · disan.example · 028 1234 5678", content.TextBody);
        });
    }

    [Fact]
    public void Mail_StepTwoAsksForSubmissionWithIdPhotosInsteadOfEkyc()
    {
        EmailContent content = HandoverInvitationEmail.Build(Invitation(), Link, null);

        Assert.Contains("nộp hồ sơ kèm ảnh căn cước hai mặt", content.TextBody);
        Assert.DoesNotContain("eKYC", content.TextBody);
    }
}
