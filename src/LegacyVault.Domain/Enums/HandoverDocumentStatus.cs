namespace LegacyVault.Domain.Enums;

/// <summary>Biên bản của một lượt bàn giao đang ở bước nào. Không có hạn chót: mỗi bước chờ tới khi người của bước đó làm xong.</summary>
public enum HandoverDocumentStatus
{
    /// <summary>Người nhận đã chọn Chấp nhận hoặc Từ chối; Phần 1 đã chép từ hồ sơ chứng tử, chờ người nhận điền Phần 2, nộp ảnh căn cước và ký.</summary>
    WaitRecipient = 0,

    /// <summary>Người nhận đã ký; chờ Người xét duyệt kiểm tra thủ công.</summary>
    WaitVerifier = 1,

    /// <summary>Người xét duyệt đã kiểm tra và gửi. Lượt đã kết thúc: tài sản được mở tải (Chấp nhận) hoặc ghi nhận từ chối.</summary>
    Completed = 2,
}
