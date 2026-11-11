using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
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

    private static HandoverInvitation FullInvitation()
    {
        HandoverInvitation invitation = new HandoverInvitation();
        invitation.FullName = "Lê Người Nhận";
        invitation.OwnerFullName = "Nguyễn Chủ Kho";
        invitation.ExecutorFullName = "Trần Thực Thi";
        invitation.ExecutorEmail = "thucthi@test.local";
        invitation.CaseCode = "01A16CF1";
        invitation.VerifierFullName = "Luật sư Dũng";
        invitation.OwnerMessage = "Giữ gìn sức khỏe.";
        invitation.ResponseDeadline = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        return invitation;
    }

    [Fact]
    public void Invitation_SubjectTitleButtonAndNoteCanBeChanged()
    {
        Set(EmailTemplateCatalog.HandoverInvitation,
            new EmailTemplateOverride(true, "Tiêu đề mới {TEN_CHU_KHO}", null, null, "Bấm vào đây", null));

        EmailContent email = HandoverInvitationEmail.Build(FullInvitation(), "https://x/claim", null);

        Assert.Equal("Tiêu đề mới Nguyễn Chủ Kho", email.Subject);
        Assert.Contains("Bấm vào đây: https://x/claim", email.TextBody);
    }

    [Fact]
    public void Invitation_AdminBody_UsesTokensAndKeepsRealValues()
    {
        Set(EmailTemplateCatalog.HandoverInvitation, new EmailTemplateOverride(
            true, null, null,
            "Chào {TEN_NGUOI_NHAN}, {TEN_CHU_KHO} nhắn bạn.\n{MA_HO_SO}\n{LOI_NHAN_CHU_KHO}\nBấm trước {HAN_PHAN_HOI}.",
            null, null));

        EmailContent email = HandoverInvitationEmail.Build(FullInvitation(), "https://x/claim", null);

        Assert.Contains("Chào Lê Người Nhận, Nguyễn Chủ Kho nhắn bạn.", email.TextBody);
        Assert.Contains("Mã hồ sơ: 01A16CF1.", email.TextBody);
        Assert.Contains("Giữ gìn sức khỏe.", email.TextBody);
        Assert.Contains("Bấm trước 17:00 ngày 20/11/2026.", email.TextBody);
        // Chữ gốc không còn khi Admin đã viết thân mới.
        Assert.DoesNotContain("bốn bước", email.TextBody);
    }

    [Fact]
    public void Invitation_SentenceTokensWithoutData_DisappearWithTheirLine()
    {
        Set(EmailTemplateCatalog.HandoverInvitation, new EmailTemplateOverride(
            true, null, null, "Dòng một.\n{MA_HO_SO}\n{NGUOI_XET_DUYET}\n{LOI_NHAN_CHU_KHO}\nDòng cuối {HAN_PHAN_HOI}.", null, null));
        HandoverInvitation invitation = FullInvitation();
        invitation.CaseCode = string.Empty;
        invitation.VerifierFullName = string.Empty;
        invitation.OwnerMessage = null;

        EmailContent email = HandoverInvitationEmail.Build(invitation, "https://x/claim", null);

        Assert.Contains("Dòng một.\nDòng cuối 17:00 ngày 20/11/2026.", email.TextBody);
        Assert.DoesNotContain("Mã hồ sơ", email.TextBody);
    }

    [Fact]
    public void Invitation_WithoutOverride_StillHasEveryStepAndNoUnreplacedToken()
    {
        EmailContent email = HandoverInvitationEmail.Build(FullInvitation(), "https://x/claim", null);

        Assert.Contains("Bạn chỉ cần làm theo bốn bước sau:", email.TextBody);
        Assert.Contains("Nhắn Trần Thực Thi xin khóa mở gói", email.TextBody);
        Assert.DoesNotContain("{", email.TextBody);
    }

    [Fact]
    public void Invitation_AdminNote_StillGetsTheScamWarningAppended()
    {
        Set(EmailTemplateCatalog.HandoverInvitation,
            new EmailTemplateOverride(true, null, null, null, null, "Ghi chú riêng của Admin."));

        EmailContent email = HandoverInvitationEmail.Build(FullInvitation(), "https://x/claim", null);

        Assert.Contains("Ghi chú riêng của Admin.", email.TextBody);
        Assert.Contains("Cảnh báo lừa đảo", email.TextBody);
    }

    [Fact]
    public void Invitation_BodyMustKeepTheDeadlineToken()
    {
        EmailTemplateDefinition definition = EmailTemplateCatalog.Find(EmailTemplateCatalog.HandoverInvitation)!;

        Assert.True(definition.EditableBody);
        Assert.Throws<AppException>(() => EmailTemplateService.CheckRequiredBodyTokens(definition, "Bấm nút để nhận."));
        EmailTemplateService.CheckRequiredBodyTokens(definition, "Bấm trước {HAN_PHAN_HOI}.");
        EmailTemplateService.CheckRequiredBodyTokens(definition, null);
    }

    [Fact]
    public void Invitation_DefaultsShowTheTemplateWithTokensAndNoScamWarning()
    {
        EmailParts parts = new EmailTemplateDefaults().Find(EmailTemplateCatalog.HandoverInvitation)!;

        Assert.Contains("{HAN_PHAN_HOI}", parts.Body);
        Assert.Contains("{MA_HO_SO}", parts.Body);
        Assert.DoesNotContain("Cảnh báo lừa đảo", parts.Note);
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
