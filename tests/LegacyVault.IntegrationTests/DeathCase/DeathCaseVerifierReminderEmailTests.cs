using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.DeathCases;
using LegacyVault.Infrastructure.Notifications;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Thư nhắc Verifier và mã hồ sơ rút gọn (OPS-04, SEC-07, TIME-01, DEATH-08). Không cần SQL.</summary>
public sealed class DeathCaseVerifierReminderEmailTests
{
    [Fact]
    public void Ops04_ReminderEmailHasCaseCodeAndDeadlineOnly()
    {
        // Chuẩn bị: hạn 03:00 UTC ngày 17/10/2026 = 10:00 giờ Việt Nam; tên có ký tự cần mã hóa HTML
        DateTimeOffset dueAt = new DateTimeOffset(2026, 10, 17, 3, 0, 0, TimeSpan.Zero);
        const string verifierName = "Lê <Xét> Hồ Sơ";
        const string link = "http://localhost:5173/login";
        const string ownerName = "Nguyễn Văn Chủ";
        const string ownerCitizenId = "012345678901";

        // Hành động
        EmailContent email = DeathCaseVerifierReminderEmail.Build(verifierName, "0000ABCD", dueAt, link);

        // Kiểm tra: chỉ có tên Verifier, mã hồ sơ, hạn theo giờ VN, link đăng nhập và câu cảnh báo giao lại
        string all = email.Subject + email.TextBody + email.HtmlBody;
        Assert.Equal("LegacyVault: hồ sơ chứng tử cần bạn xét trước hạn", email.Subject);
        Assert.Contains("0000ABCD", email.TextBody);
        Assert.Contains("10:00 ngày 17/10/2026", email.TextBody);
        Assert.Contains(link, email.TextBody);
        Assert.Contains("Quá hạn hồ sơ sẽ được giao cho Verifier khác.", email.TextBody);
        Assert.DoesNotContain(ownerName, all);
        Assert.DoesNotContain(ownerCitizenId, all);
        Assert.Contains(verifierName, email.TextBody);
        Assert.Contains("&lt;X", email.HtmlBody);
        Assert.Contains("t&gt;", email.HtmlBody);
        Assert.DoesNotContain("<X", email.HtmlBody);
    }

    [Fact]
    public void Death08_CaseCodeUnchangedAfterMove()
    {
        // Chuẩn bị: Id cố định, 8 ký tự hex cuối là "0000abcd"
        Guid caseId = new Guid("0192f0a1-0000-7000-8000-00000000abcd");

        // Hành động
        string code = DeathCaseRules.CaseCode(caseId);

        // Kiểm tra: 8 ký tự hex cuối, chữ hoa (giữ đúng thuật toán cũ của DeathCaseNoticeJob)
        Assert.Equal("0000ABCD", code);
    }
}
