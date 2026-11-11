using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Handover;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Chữ gốc của từng loại thư, lấy thẳng từ các hàm dựng thư để không bị lệch với thư thật. Biến giữ dạng {TEN_...}.</summary>
public sealed class EmailTemplateDefaults : IEmailTemplateDefaults
{
    public EmailParts? Find(string key)
    {
        switch (key)
        {
            case EmailTemplateCatalog.PlanActivated:
                return PlanActivatedNoticeEmail.SampleParts();
            case EmailTemplateCatalog.HandoverInvitation:
                return HandoverInvitationEmail.SampleParts();
            case EmailTemplateCatalog.DocVerifierTurn:
                return HandoverDocumentNoticeEmail.SampleParts(HandoverDocumentNoticeKind.VerifierTurn);
            case EmailTemplateCatalog.DocRecipientReturned:
                return HandoverDocumentNoticeEmail.SampleParts(HandoverDocumentNoticeKind.RecipientReturned);
            case EmailTemplateCatalog.DocCompleted:
                return HandoverDocumentNoticeEmail.SampleParts(HandoverDocumentNoticeKind.Completed);
            case EmailTemplateCatalog.ExecutorOverdue:
                return ExecutorOverdueNoticeEmail.DefaultParts(false);
            case EmailTemplateCatalog.ExecutorOverdueReminder:
                return ExecutorOverdueNoticeEmail.DefaultParts(true);
            case EmailTemplateCatalog.CheckInDueFirst:
                return CheckInReminderEmail.DefaultParts(CheckInReminderKind.DueInSevenDays, "{HAN_DIEM_DANH}", "{HET_THOI_GIAN_CHO}");
            case EmailTemplateCatalog.CheckInDueSecond:
                return CheckInReminderEmail.DefaultParts(CheckInReminderKind.DueInOneDay, "{HAN_DIEM_DANH}", "{HET_THOI_GIAN_CHO}");
            case EmailTemplateCatalog.CheckInLate:
                return CheckInReminderEmail.DefaultParts(CheckInReminderKind.LateReminder1, "{HAN_DIEM_DANH}", "{HET_THOI_GIAN_CHO}");
            default:
                return null;
        }
    }
}
