namespace LegacyVault.Domain.Entities;

/// <summary>Trạng thái một hồ sơ người nhận.</summary>
public enum HandoverSubmissionStatus
{
    /// <summary>Đã nộp, chờ Người xét duyệt.</summary>
    Submitted = 0,

    /// <summary>Người xét duyệt yêu cầu bổ sung; người nhận nộp lại bản mới.</summary>
    SupplementRequested = 1,

    /// <summary>Người xét duyệt đã duyệt; lượt sang bước quyết định.</summary>
    Approved = 2,

    /// <summary>Bị bản nộp lại thay thế.</summary>
    Superseded = 3,
}
