using LegacyVault.Infrastructure.Notifications;
using Xunit;

namespace LegacyVault.IntegrationTests.Email;

/// <summary>
/// Kiểm tra nội dung email thông báo Executor khi Chủ kho quá hạn điểm danh (DMS-05).
/// Hàm thuần, không cần SQL hay mạng.
/// </summary>
public sealed class ExecutorOverdueNoticeEmailTests
{
    private const string Link = "http://localhost:5173/executor?vault=11112222-3333-4444-5555-666677778888";

    [Fact]
    public void Build_InitialNotice_HasExpectedSubjectAndContent()
    {
        const string executorName = "Trần <Người> Thực Hiện";
        const string ownerCitizenId = "012345678901";
        const string ownerSecretAsset = "10 BTC trong ví lạnh";

        EmailContent email = ExecutorOverdueNoticeEmail.Build(executorName, Link, isReminder: false);

        string all = email.Subject + email.TextBody + email.HtmlBody;

        Assert.Equal("LegacyVault: chủ kho quá hạn điểm danh, cần bạn kiểm tra", email.Subject);
        Assert.Contains(executorName, email.TextBody);
        Assert.Contains("quá hạn điểm danh và hết thời gian chờ", email.TextBody);
        Assert.Contains("không tự động mở kho", email.TextBody);
        Assert.Contains(Link, email.TextBody);
        Assert.Contains(Link, email.HtmlBody);

        // Bảo mật OPS-04/SEC-07: không lộ CCCD hay tài sản chủ kho
        Assert.DoesNotContain(ownerCitizenId, all);
        Assert.DoesNotContain(ownerSecretAsset, all);

        // Mã hóa HTML an toàn chống XSS
        Assert.Contains("&lt;Người&gt;", email.HtmlBody);
        Assert.DoesNotContain("<Người>", email.HtmlBody);

        // Đúng chuẩn LegacyVaultEmailTheme (có logo CID)
        Assert.Contains(LegacyVaultEmailTheme.LogoContentId, email.HtmlBody);
    }

    [Fact]
    public void Build_Reminder_HasReminderSubjectAndText()
    {
        EmailContent email = ExecutorOverdueNoticeEmail.Build("Lê Văn B", Link, isReminder: true);

        Assert.Contains("Nhắc lại", email.Subject);
        Assert.Contains("Nhắc lại:", email.TextBody);
        Assert.Contains(Link, email.TextBody);
    }
}
