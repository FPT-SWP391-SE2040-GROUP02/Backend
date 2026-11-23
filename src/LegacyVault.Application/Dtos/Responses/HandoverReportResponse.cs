namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Hồ sơ báo tin nhìn từ người nộp: không cho biết có tìm thấy kho hay không.</summary>
public class MyHandoverReportResponse
{
    public Guid Id { get; set; }

    public string DeceasedFullName { get; set; } = string.Empty;

    /// <summary>SUBMITTED, SUPPLEMENT_REQUESTED, ACCEPTED hoặc REJECTED.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Chủ kho đã hủy biên bản sau khi hồ sơ được chấp nhận.</summary>
    public bool CancelledByOwner { get; set; }

    public string Note { get; set; } = string.Empty;

    public string? ReviewReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public List<SuggestedRecipientResponse> Recipients { get; set; } = new List<SuggestedRecipientResponse>();

    public List<HandoverReportFileResponse> Files { get; set; } = new List<HandoverReportFileResponse>();
}

/// <summary>Hồ sơ báo tin nhìn từ luật sư được giao: thêm người nộp và kết quả tìm kho.</summary>
public class VerifierHandoverReportResponse : MyHandoverReportResponse
{
    public string DeceasedCitizenId { get; set; } = string.Empty;

    public string SubmitterName { get; set; } = string.Empty;

    public string SubmitterEmail { get; set; } = string.Empty;

    public string SubmitterPhone { get; set; } = string.Empty;

    /// <summary>Có kho mà Chủ kho đã eKYC mang đúng số căn cước này.</summary>
    public bool VaultFound { get; set; }

    /// <summary>Tên Chủ kho của kho tìm thấy; rỗng khi không có.</summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Họ tên người nộp khai khớp họ tên eKYC của Chủ kho.</summary>
    public bool NameMatches { get; set; }

    public Guid? RecordId { get; set; }
}

public class SuggestedRecipientResponse
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;
}

/// <summary>Một tệp giấy tờ: chỉ tên và kích thước, nội dung tải qua API riêng có kiểm quyền.</summary>
public class HandoverReportFileResponse : HandoverRecordFileResponse
{
    /// <summary>DEATH_CERTIFICATE, WILL hoặc OTHER.</summary>
    public string Kind { get; set; } = string.Empty;
}
