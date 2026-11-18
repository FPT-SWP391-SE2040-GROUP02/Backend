namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một dòng trong hàng chờ duyệt hồ sơ người nhận của Người xét duyệt.</summary>
public class VerifierHandoverSubmissionSummaryResponse
{
    public Guid Id { get; set; }

    public Guid TurnId { get; set; }

    public string ContainerName { get; set; } = string.Empty;

    /// <summary>ACCEPT | DECLINE.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>true khi cả ba trường (họ tên, số căn cước, ngày sinh) khớp thông tin Chủ kho đã khai.</summary>
    public bool AllMatch { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
}
