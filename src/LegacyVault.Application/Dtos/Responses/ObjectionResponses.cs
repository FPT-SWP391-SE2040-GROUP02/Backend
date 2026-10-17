namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Báo "Tôi còn sống" (F2C) nhìn từ phía Owner và Verifier.</summary>
public class ObjectionResponse
{
    public Guid Id { get; set; }

    /// <summary>OPEN | CONCLUDED.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset FirstReportedAt { get; set; }

    public DateTimeOffset LastReportedAt { get; set; }

    public int ReportCount { get; set; }

    /// <summary>Hạn chụp selfie (7 ngày từ lần báo đầu, không đặt lại).</summary>
    public DateTimeOffset SelfieDeadlineAt { get; set; }

    public bool SelfieWindowOpen { get; set; }

    public bool SelfiePassed { get; set; }

    public DateTimeOffset? SelfiePassedAt { get; set; }

    /// <summary>Số lần còn được thử trong cửa sổ 24 giờ.</summary>
    public int SelfieAttemptsRemaining { get; set; }

    public DateTimeOffset? VerifierDueAt { get; set; }

    public int InsufficientEvidenceCount { get; set; }

    /// <summary>ALIVE_CONFIRMED | OBJECTION_REJECTED | INSUFFICIENT_EVIDENCE; null khi chưa có.</summary>
    public string? Conclusion { get; set; }

    public string? ConclusionReason { get; set; }

    public DateTimeOffset? ConcludedAt { get; set; }

    public DateTimeOffset ServerTime { get; set; }
}

/// <summary>Hồ sơ chứng tử của kho nhìn từ phía Owner (không có nội dung khai báo).</summary>
public class OwnerDeathCaseResponse
{
    public Guid CaseId { get; set; }

    public string CaseCode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? ObjectionStartsAt { get; set; }

    public DateTimeOffset? ObjectionEndsAt { get; set; }

    /// <summary>Báo còn sống gần nhất của hồ sơ; null khi chưa báo.</summary>
    public ObjectionResponse? Objection { get; set; }
}
