namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Biên bản bàn giao của một kho, dùng chung cho luật sư, Người thực thi và người nhận.
/// Người nhận chỉ thấy dòng của chính mình; luật sư thấy thêm danh sách tài sản, người nhận và biên bản phân chia của Chủ kho.
/// </summary>
public class HandoverRecordResponse
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public string VaultName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>DRAFTING, IDENTITY_CHECK, SIGNING, COMPLETED.</summary>
    public string Status { get; set; } = string.Empty;

    public int RevisionNo { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Lúc luật sư xác nhận căn cứ pháp lý khi chốt; null khi đang lập (mở lại thì về null).</summary>
    public DateTimeOffset? LegalBasisConfirmedAt { get; set; }

    /// <summary>Luật sư đã xác nhận căn cứ pháp lý; null cùng lúc với LegalBasisConfirmedAt.</summary>
    public Guid? LegalBasisConfirmedByUserId { get; set; }

    /// <summary>Họ tên luật sư đã xác nhận căn cứ pháp lý (để in biên bản); rỗng khi chưa xác nhận.</summary>
    public string LegalBasisConfirmedByName { get; set; } = string.Empty;

    public string VerifierName { get; set; } = string.Empty;
    public string ExecutorName { get; set; } = string.Empty;
    public DateTimeOffset? VerifierSignedAt { get; set; }
    public DateTimeOffset? ExecutorSignedAt { get; set; }

    public List<HandoverRecordRecipientResponse> Recipients { get; set; } = new List<HandoverRecordRecipientResponse>();

    /// <summary>Người đang xem ký được lúc này không (đúng bước, chưa ký bản hiện tại).</summary>
    public bool CanSign { get; set; }

    /// <summary>Chỉ cho luật sư: tài sản của kho để chọn.</summary>
    public List<HandoverRecordAssetResponse> AvailableAssets { get; set; } = new List<HandoverRecordAssetResponse>();

    /// <summary>Chỉ cho luật sư: người nhận Chủ kho đã khai.</summary>
    public List<HandoverRecordPersonResponse> AvailableRecipients { get; set; } = new List<HandoverRecordPersonResponse>();

    /// <summary>Chỉ cho luật sư: biên bản phân chia của Chủ kho, gom theo người nhận. Rỗng khi Chủ kho chưa gán tài sản nào.</summary>
    public List<OwnerSplitRecipientResponse> OwnerSplit { get; set; } = new List<OwnerSplitRecipientResponse>();

    /// <summary>Yêu cầu nhận tài sản kèm giấy tờ; chỉ luật sư của biên bản thấy, các vai khác nhận danh sách rỗng.</summary>
    public List<HandoverClaimResponse> Claims { get; set; } = new List<HandoverClaimResponse>();

    /// <summary>Tệp căn cứ luật sư đính kèm biên bản; chỉ luật sư thấy.</summary>
    public List<HandoverRecordFileResponse> EvidenceFiles { get; set; } = new List<HandoverRecordFileResponse>();
}

/// <summary>Một người nhận trong biên bản, kèm tài sản được nhận và tình trạng danh tính, chữ ký.</summary>
public class HandoverRecordRecipientResponse
{
    public Guid BeneficiaryId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string CitizenId { get; set; } = string.Empty;
    public List<HandoverRecordAssetResponse> Assets { get; set; } = new List<HandoverRecordAssetResponse>();

    /// <summary>Lượt bàn giao của người này; null khi biên bản chưa chốt lần nào.</summary>
    public Guid? TurnId { get; set; }

    /// <summary>Trạng thái lượt (INVITED, IDENTITY_IN_PROGRESS, READY_TO_ACCEPT, HANDOVER_COMMITTED...); rỗng khi chưa có lượt.</summary>
    public string TurnStatus { get; set; } = string.Empty;

    /// <summary>Người nhận đã mở link và gắn tài khoản chưa.</summary>
    public bool Claimed { get; set; }

    /// <summary>Hồ sơ danh tính mới nhất (ảnh căn cước) người nhận đã nộp; null khi chưa nộp.</summary>
    public Guid? SubmissionId { get; set; }

    /// <summary>SUBMITTED, SUPPLEMENT_REQUIRED, APPROVED; rỗng khi chưa nộp.</summary>
    public string SubmissionStatus { get; set; } = string.Empty;

    public DateTimeOffset? IdentityConfirmedAt { get; set; }
    public DateTimeOffset? SignedAt { get; set; }

    /// <summary>Phiếu giao nhận: lúc bên nhận xác nhận đã nhận đủ; null khi chưa.</summary>
    public DateTimeOffset? ReceivedAllAt { get; set; }

    /// <summary>Phiếu giao nhận: lúc Người thực thi ký bên giao; null khi chưa.</summary>
    public DateTimeOffset? ReceiptExecutorSignedAt { get; set; }

    /// <summary>Người nhận báo thiếu hoặc sai; null khi không báo.</summary>
    public string? ReceiptIssueNote { get; set; }

    public DateTimeOffset? ReceiptIssueAt { get; set; }
}

public class HandoverRecordAssetResponse
{
    public Guid AssetId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>FILE, NOTE...</summary>
    public string Kind { get; set; } = string.Empty;
}

public class HandoverRecordPersonResponse
{
    public Guid BeneficiaryId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string CitizenId { get; set; } = string.Empty;
}

/// <summary>Một người nhận trong biên bản phân chia của Chủ kho và các tài sản Chủ kho đã gán cho người đó.</summary>
public class OwnerSplitRecipientResponse
{
    public Guid BeneficiaryId { get; set; }
    public List<Guid> AssetIds { get; set; } = new List<Guid>();
}

/// <summary>Một dòng trong danh sách biên bản của luật sư.</summary>
public class HandoverRecordSummaryResponse
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public string VaultName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int RecipientCount { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
}
