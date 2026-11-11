using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Handover;
using LegacyVault.Infrastructure.Notifications;
using Xunit;

namespace LegacyVault.IntegrationTests.Email;

/// <summary>Bản chụp tĩnh nên nhóm này không chạy song song với test khác.</summary>
[CollectionDefinition("EmailTemplateOverrides", DisableParallelization = true)]
public sealed class EmailTemplateOverridesCollection
{
}

/// <summary>Chữ thư Admin đã sửa được dùng khi dựng thư; chưa sửa thì giữ chữ gốc. Hàm thuần, không cần SQL. Chạy tuần tự vì dùng bản chụp tĩnh.</summary>
[Collection("EmailTemplateOverrides")]
public sealed class EmailTemplateOverrideTests : IDisposable
{
    public EmailTemplateOverrideTests()
    {
        EmailTemplateOverrides.Replace(new Dictionary<string, EmailTemplateOverride>());
    }

    public void Dispose()
    {
        EmailTemplateOverrides.Replace(new Dictionary<string, EmailTemplateOverride>());
    }

    private static void Set(string key, EmailTemplateOverride value)
    {
        EmailTemplateOverrides.Replace(new Dictionary<string, EmailTemplateOverride> { [key] = value });
    }

    [Fact]
    public void Build_WithoutOverride_KeepsOriginalText()
    {
        EmailContent email = ExecutorOverdueNoticeEmail.Build("Trần Thực Thi", "https://x/executor");

        Assert.Equal(ExecutorOverdueNoticeEmail.DefaultParts(false).Subject, email.Subject);
        Assert.Equal(EmailTemplateCatalog.ExecutorOverdue, email.TemplateKey);
    }

    [Fact]
    public void Build_WithOverride_UsesAdminTextAndFillsTokens()
    {
        Set(EmailTemplateCatalog.ExecutorOverdue,
            new EmailTemplateOverride(true, "Chào {TEN_NGUOI_NHAN}, cần bạn xem", null, "Nội dung riêng cho {TEN_NGUOI_NHAN}.", "Mở ngay", null));

        EmailContent email = ExecutorOverdueNoticeEmail.Build("Trần Thực Thi", "https://x/executor");

        Assert.Equal("Chào Trần Thực Thi, cần bạn xem", email.Subject);
        Assert.Contains("Nội dung riêng cho Trần Thực Thi.", email.TextBody);
        Assert.Contains("Mở ngay: https://x/executor", email.TextBody);
        // Trường để trống giữ chữ gốc.
        Assert.Contains(ExecutorOverdueNoticeEmail.DefaultParts(false).Note, email.TextBody);
    }

    [Fact]
    public void Build_OverrideOfOneKind_DoesNotChangeTheOther()
    {
        Set(EmailTemplateCatalog.ExecutorOverdueReminder,
            new EmailTemplateOverride(true, "Chỉ thư nhắc lại", null, null, null, null));

        Assert.Equal(ExecutorOverdueNoticeEmail.DefaultParts(false).Subject, ExecutorOverdueNoticeEmail.Build("A", "u").Subject);
        Assert.Equal("Chỉ thư nhắc lại", ExecutorOverdueNoticeEmail.Build("A", "u", true).Subject);
    }

    [Fact]
    public void IsEnabled_OnlyHonoursTheSwitchForTypesThatMayBeDisabled()
    {
        EmailTemplateOverrides.Replace(new Dictionary<string, EmailTemplateOverride>
        {
            [EmailTemplateCatalog.CheckInDueFirst] = new EmailTemplateOverride(false, null, null, null, null, null),
            [EmailTemplateCatalog.HandoverInvitation] = new EmailTemplateOverride(false, null, null, null, null, null),
        });

        Assert.False(EmailTemplateOverrides.IsEnabled(EmailTemplateCatalog.CheckInDueFirst));
        // Thư mời nhận tài sản không được tắt dù dòng ghi Enabled = false.
        Assert.True(EmailTemplateOverrides.IsEnabled(EmailTemplateCatalog.HandoverInvitation));
        Assert.True(EmailTemplateOverrides.IsEnabled(EmailTemplateCatalog.PlanActivated));
    }

    [Fact]
    public void Invitation_OnlySubjectTitleButtonAndNoteCanBeChanged_BodyStays()
    {
        Set(EmailTemplateCatalog.HandoverInvitation,
            new EmailTemplateOverride(true, "Tiêu đề mới {TEN_CHU_KHO}", null, "Thân thư không được đổi", "Bấm vào đây", null));
        HandoverInvitation invitation = new HandoverInvitation();
        invitation.FullName = "Lê Người Nhận";
        invitation.OwnerFullName = "Nguyễn Chủ Kho";
        invitation.ResponseDeadline = DateTimeOffset.UtcNow.AddDays(3);

        EmailContent email = HandoverInvitationEmail.Build(invitation, "https://x/claim", null);

        Assert.Equal("Tiêu đề mới Nguyễn Chủ Kho", email.Subject);
        Assert.DoesNotContain("Thân thư không được đổi", email.TextBody);
        Assert.Contains("Bấm vào đây: https://x/claim", email.TextBody);
    }

    [Fact]
    public void CheckIn_UsesOneKeyPerMilestone()
    {
        Assert.Equal(EmailTemplateCatalog.CheckInDueFirst, CheckInReminderEmail.KeyFor(CheckInReminderKind.DueInSevenDays));
        Assert.Equal(EmailTemplateCatalog.CheckInDueSecond, CheckInReminderEmail.KeyFor(CheckInReminderKind.DueInOneDay));
        Assert.Equal(EmailTemplateCatalog.CheckInLate, CheckInReminderEmail.KeyFor(CheckInReminderKind.LateReminder3));
    }

    [Fact]
    public void Defaults_EveryCatalogTypeHasOriginalTextWithPlaceholdersKept()
    {
        EmailTemplateDefaults defaults = new EmailTemplateDefaults();
        foreach (EmailTemplateDefinition definition in EmailTemplateCatalog.All)
        {
            EmailParts? parts = defaults.Find(definition.Key);
            Assert.NotNull(parts);
            Assert.False(string.IsNullOrWhiteSpace(parts!.Subject), definition.Key);
        }
        Assert.Contains("{MA_BIEN_BAN}", defaults.Find(EmailTemplateCatalog.DocCompleted)!.Body);
        Assert.Contains("{TEN_CHU_KHO}", defaults.Find(EmailTemplateCatalog.PlanActivated)!.Body);
    }
}
