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
            default:
                return null;
        }
    }
}
