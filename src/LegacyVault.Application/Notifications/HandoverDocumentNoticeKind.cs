namespace LegacyVault.Application.Notifications;

/// <summary>Biên bản vừa chuyển sang bước nào, để chọn nội dung thư báo cho đúng người.</summary>
public enum HandoverDocumentNoticeKind
{
    /// <summary>Gửi Người xét duyệt: người nhận đã ký, cần kiểm tra thủ công.</summary>
    VerifierTurn = 0,

    /// <summary>Gửi người nhận: Người xét duyệt trả về để bổ sung.</summary>
    RecipientReturned = 1,

    /// <summary>Gửi người nhận và Người thực thi: biên bản hoàn tất.</summary>
    Completed = 2,
}
