using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.DeathCases;
using LegacyVault.Infrastructure.Notifications;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Thư báo Executor khi hồ sơ cần bổ sung hoặc bị từ chối. Không cần SQL.</summary>
public sealed class DeathCaseExecutorNoticeEmailTests
{
    private const string Link = "http://localhost:5173/executor/claim";

    [Fact]
    public void SupplementEmail_HasCaseCodeAndDeadlineOnly()
    {
        // Chuẩn bị: hạn 03:00 UTC ngày 26/10/2026 = 10:00 giờ Việt Nam; tên có ký tự cần mã hóa HTML
        DateTimeOffset dueAt = new DateTimeOffset(2026, 10, 26, 3, 0, 0, TimeSpan.Zero);
        const string executorName = "Trần <Thi> Hành";
        const string ownerName = "Nguyễn Văn Chủ";
        const string ownerCitizenId = "012345678901";

        // Hành động
        EmailContent email = DeathCaseExecutorNoticeEmail.Build(
            executorName, "0000ABCD", DeathCaseDecisionKind.SupplementRequested, dueAt, Link);

        // Kiểm tra: chỉ có tên Executor, mã hồ sơ, hạn theo giờ Việt Nam và link trang Executor
        string all = email.Subject + email.TextBody + email.HtmlBody;
        Assert.Equal("LegacyVault: hồ sơ chứng tử cần bổ sung", email.Subject);
        Assert.Contains("0000ABCD", email.TextBody);
        Assert.Contains("10:00 ngày 26/10/2026", email.TextBody);
        Assert.Contains(Link, email.TextBody);
        Assert.DoesNotContain(ownerName, all);
        Assert.DoesNotContain(ownerCitizenId, all);
        Assert.Contains(executorName, email.TextBody);
        Assert.Contains("&lt;Thi&gt;", email.HtmlBody);
        Assert.DoesNotContain("<Thi>", email.HtmlBody);
    }

    [Fact]
    public void RejectedEmail_TellsExecutorToReadReasonInApp()
    {
        // Hành động
        EmailContent email = DeathCaseExecutorNoticeEmail.Build(
            "Trần Thi Hành", "0000ABCD", DeathCaseDecisionKind.Rejected, null, Link);

        // Kiểm tra: không có hạn, chỉ dẫn đăng nhập xem lý do
        Assert.Equal("LegacyVault: hồ sơ chứng tử đã bị từ chối", email.Subject);
        Assert.Contains("0000ABCD", email.TextBody);
        Assert.Contains("xem lý do", email.TextBody);
        Assert.Contains(Link, email.TextBody);
        Assert.DoesNotContain("trước ", email.TextBody);
    }
}
