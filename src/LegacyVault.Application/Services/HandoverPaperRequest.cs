using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Dữ liệu để dựng một ảnh văn bản: hồ sơ người nhận, lượt của hồ sơ và, nếu có, biên bản đã ký và Người xét duyệt đã ký.
/// </summary>
public class HandoverPaperRequest
{
    public HandoverSubmission Submission { get; set; } = null!;

    public HandoverTurn Turn { get; set; } = null!;

    /// <summary>Người xét duyệt đã ký; để trống khi dựng lúc người nhận nộp (chưa có Người xét duyệt).</summary>
    public Guid? ReviewerId { get; set; }

    /// <summary>Lúc Người xét duyệt ký; để trống khi dựng lúc người nhận nộp.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>Biên bản đã ký (luồng Biên Bản); để trống ở luồng cũ.</summary>
    public HandoverDocument? Document { get; set; }
}
