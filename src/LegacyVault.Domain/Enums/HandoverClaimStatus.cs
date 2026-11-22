namespace LegacyVault.Domain.Enums;

/// <summary>
/// Kết quả luật sư xét giấy tờ của một yêu cầu nhận tài sản. Giá trị lưu trong DB là tên enum; không đổi tên.
/// </summary>
public enum HandoverClaimStatus
{
    /// <summary>Người nhận vừa gửi hoặc vừa nộp bổ sung, chờ luật sư xét.</summary>
    Submitted = 0,

    /// <summary>Luật sư cần thêm giấy tờ; người nhận nộp thêm thì quay về Submitted.</summary>
    SupplementRequested = 1,

    /// <summary>Luật sư chấp nhận giấy tờ; người này được ghi vào biên bản.</summary>
    Accepted = 2,

    /// <summary>Luật sư từ chối có lý do; người này không được ghi vào biên bản.</summary>
    Rejected = 3,
}
