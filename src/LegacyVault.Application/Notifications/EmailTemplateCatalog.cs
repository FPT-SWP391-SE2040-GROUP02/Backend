namespace LegacyVault.Application.Notifications;

/// <summary>Một biến Admin được chèn vào chữ thư, viết {TEN_BIEN}.</summary>
public sealed record EmailTemplateToken(string Name, string Description);

/// <summary>Mô tả một loại thư thông báo mà Admin chỉnh được.</summary>
public sealed record EmailTemplateDefinition(
    string Key,
    string Label,
    string Description,
    bool CanDisable,
    bool EditableBody,
    IReadOnlyList<EmailTemplateToken> Tokens);

/// <summary>Danh sách loại thư Admin chỉnh được. Thêm loại mới: thêm ở đây rồi nối hàm dựng thư với EmailTemplateOverrides.</summary>
public static class EmailTemplateCatalog
{
    public const string PlanActivated = "PLAN_ACTIVATED";
    public const string HandoverInvitation = "HANDOVER_INVITATION";

    private static readonly EmailTemplateToken Recipient = new EmailTemplateToken("TEN_NGUOI_NHAN", "Họ tên người nhận thư");

    public static readonly IReadOnlyList<EmailTemplateDefinition> All = new List<EmailTemplateDefinition>
    {
        new EmailTemplateDefinition(PlanActivated, "Báo người nhận khi Chủ kho kích hoạt kế hoạch",
            "Gửi cho từng người nhận ngay khi Chủ kho kích hoạt kế hoạch bàn giao.", true, true, new[]
            {
                Recipient,
                new EmailTemplateToken("TEN_CHU_KHO", "Họ tên Chủ kho"),
                new EmailTemplateToken("EMAIL_CHU_KHO", "Email Chủ kho"),
                new EmailTemplateToken("SDT_CHU_KHO", "Số điện thoại Chủ kho"),
                new EmailTemplateToken("TEN_NGUOI_THUC_THI", "Họ tên Người thực thi"),
                new EmailTemplateToken("EMAIL_NGUOI_THUC_THI", "Email Người thực thi"),
                new EmailTemplateToken("SDT_NGUOI_THUC_THI", "Số điện thoại Người thực thi"),
            }),
        new EmailTemplateDefinition(HandoverInvitation, "Thư mời nhận tài sản",
            "Thư có liên kết riêng để người nhận bắt đầu nhận bàn giao. Phần thân thư theo từng hồ sơ nên chỉ sửa được tiêu đề, nút và ghi chú; không tắt được.",
            false, false, new[]
            {
                Recipient,
                new EmailTemplateToken("TEN_CHU_KHO", "Họ tên Chủ kho"),
                new EmailTemplateToken("HAN_PHAN_HOI", "Hạn phản hồi đầy đủ, ví dụ 17:00 ngày 20/11/2026"),
                new EmailTemplateToken("HAN_NGAN", "Hạn phản hồi dạng ngày/tháng, ví dụ 20/11"),
            }),
    };

    public static EmailTemplateDefinition? Find(string key)
    {
        foreach (EmailTemplateDefinition definition in All)
        {
            if (definition.Key == key)
            {
                return definition;
            }
        }
        return null;
    }

    public static bool CanDisable(string key)
    {
        EmailTemplateDefinition? found = Find(key);
        return found != null && found.CanDisable;
    }

    public static bool HasEditableBody(string key)
    {
        EmailTemplateDefinition? found = Find(key);
        return found != null && found.EditableBody;
    }


}
