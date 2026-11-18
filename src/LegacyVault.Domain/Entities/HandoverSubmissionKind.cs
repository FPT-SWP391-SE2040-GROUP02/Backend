namespace LegacyVault.Domain.Entities;

/// <summary>Người nhận chọn gì trong hồ sơ nộp cho Người xét duyệt.</summary>
public enum HandoverSubmissionKind
{
    /// <summary>Chấp nhận nhận tài sản của gói.</summary>
    Accept = 0,

    /// <summary>Từ chối nhận; tài sản của gói coi như không giao cho ai.</summary>
    Decline = 1,
}
