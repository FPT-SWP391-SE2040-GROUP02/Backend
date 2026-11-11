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
    public const string DocVerifierTurn = "DOC_VERIFIER_TURN";
    public const string DocRecipientReturned = "DOC_RECIPIENT_RETURNED";
    public const string DocCompleted = "DOC_COMPLETED";
    public const string ExecutorOverdue = "EXECUTOR_OVERDUE";
    public const string ExecutorOverdueReminder = "EXECUTOR_OVERDUE_REMINDER";
    public const string CheckInDueFirst = "CHECKIN_DUE_FIRST";
    public const string CheckInDueSecond = "CHECKIN_DUE_SECOND";
    public const string CheckInLate = "CHECKIN_LATE";

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
        DocDefinition(DocVerifierTurn, "Biên bản đã ký, chờ Người xét duyệt kiểm tra",
            "Gửi Người xét duyệt khi người nhận đã ký biên bản."),
        DocDefinition(DocRecipientReturned, "Biên bản được trả về cho người nhận",
            "Gửi người nhận khi Người xét duyệt trả biên bản để bổ sung."),
        DocDefinition(DocCompleted, "Biên bản đã hoàn tất",
            "Gửi khi biên bản đồng ý hoặc văn bản từ chối đã hoàn tất."),
        OverdueDefinition(ExecutorOverdue, "Báo Người thực thi: Chủ kho quá hạn điểm danh"),
        OverdueDefinition(ExecutorOverdueReminder, "Nhắc lại Người thực thi: Chủ kho quá hạn điểm danh"),
        CheckInDefinition(CheckInDueFirst, "Nhắc điểm danh: lần nhắc đầu trước hạn"),
        CheckInDefinition(CheckInDueSecond, "Nhắc điểm danh: lần nhắc thứ hai trước hạn"),
        CheckInDefinition(CheckInLate, "Nhắc điểm danh: nhắc trễ khi đã quá hạn (lần 1 đến 5)"),
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

    private static EmailTemplateDefinition DocDefinition(string key, string label, string description)
    {
        return new EmailTemplateDefinition(key, label, description, true, true, new[]
        {
            Recipient,
            new EmailTemplateToken("MA_BIEN_BAN", "Mã biên bản"),
            new EmailTemplateToken("TEN_BEN_NHAN", "Họ tên người nhận di sản trong biên bản"),
            new EmailTemplateToken("LY_DO", "Lý do trả biên bản (chỉ có ở thư trả về)"),
        });
    }

    private static EmailTemplateDefinition OverdueDefinition(string key, string label)
    {
        return new EmailTemplateDefinition(key, label,
            "Gửi Người thực thi khi Chủ kho hết thời gian chờ điểm danh.", true, true, new[] { Recipient });
    }

    private static EmailTemplateDefinition CheckInDefinition(string key, string label)
    {
        return new EmailTemplateDefinition(key, label,
            "Gửi Chủ kho để nhắc điểm danh.", true, true, new[]
            {
                Recipient,
                new EmailTemplateToken("HAN_DIEM_DANH", "Hạn điểm danh, ví dụ 17:00 ngày 20/11/2026"),
                new EmailTemplateToken("HET_THOI_GIAN_CHO", "Lúc hết thời gian chờ"),
            });
    }
}
