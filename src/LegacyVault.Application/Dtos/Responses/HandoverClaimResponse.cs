namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một yêu cầu nhận tài sản trong biên bản, kèm giấy tờ người nhận đã nộp (màn luật sư).</summary>
public class HandoverClaimResponse
{
    public Guid Id { get; set; }

    public Guid BeneficiaryId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string CitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>SUBMITTED, SUPPLEMENT_REQUESTED, ACCEPTED hoặc REJECTED.</summary>
    public string Status { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public string? ReviewReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public List<HandoverRecordFileResponse> Files { get; set; } = new List<HandoverRecordFileResponse>();
}

/// <summary>Một tệp đính kèm biên bản: chỉ tên và kích thước, nội dung tải qua API riêng có kiểm quyền.</summary>
public class HandoverRecordFileResponse
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Thông tin link riêng gửi yêu cầu nhận tài sản (GET /handover/requests/preview, không cần đăng nhập).</summary>
public class HandoverRequestPreviewResponse
{
    public string OwnerName { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    /// <summary>Email Chủ kho đã khai cho người nhận, che bớt; người nhận phải đăng nhập đúng email này.</summary>
    public string InvitedEmailMasked { get; set; } = string.Empty;

    /// <summary>Gửi yêu cầu được không: kế hoạch đang hoạt động hoặc đang có biên bản chưa đủ chữ ký.</summary>
    public bool CanRequest { get; set; }
}

/// <summary>Một yêu cầu nhận tài sản của người đang đăng nhập (GET /handover/requests/mine).</summary>
public class MyHandoverClaimResponse
{
    public Guid Id { get; set; }

    public Guid RecordId { get; set; }

    public string OwnerName { get; set; } = string.Empty;

    /// <summary>SUBMITTED, SUPPLEMENT_REQUESTED, ACCEPTED hoặc REJECTED.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Bước của biên bản: DRAFTING, IDENTITY_CHECK, SIGNING, COMPLETED hoặc CANCELLED.</summary>
    public string RecordStatus { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public string? ReviewReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public List<HandoverRecordFileResponse> Files { get; set; } = new List<HandoverRecordFileResponse>();
}

/// <summary>Yêu cầu nhận tài sản đang mở của kho, cho Chủ kho xem và hủy (GET /vaults/{id}/handover-request).</summary>
public class OwnerHandoverRequestResponse
{
    public Guid RecordId { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Họ tên những người nhận đã gửi yêu cầu.</summary>
    public List<string> RequesterNames { get; set; } = new List<string>();

    /// <summary>Hủy được tới khi biên bản đủ chữ ký.</summary>
    public bool CanCancel { get; set; }
}
