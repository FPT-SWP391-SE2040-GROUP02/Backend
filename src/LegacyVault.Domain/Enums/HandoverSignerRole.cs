namespace LegacyVault.Domain.Enums;

/// <summary>Người ký biên bản bàn giao. Hiện chỉ luật sư ký; hai vai còn lại giữ để đọc chữ ký cũ.</summary>
public enum HandoverSignerRole
{
    /// <summary>Luật sư (Người xét duyệt) ký xác minh.</summary>
    Verifier = 0,

    /// <summary>Người thực thi, người giữ và giao tài sản.</summary>
    Executor = 1,

    /// <summary>Một người nhận có tên trong biên bản.</summary>
    Recipient = 2,
}
