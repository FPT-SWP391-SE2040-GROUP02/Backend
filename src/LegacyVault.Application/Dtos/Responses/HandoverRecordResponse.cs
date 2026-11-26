namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Biên bản bàn giao của một kho, dùng chung cho luật sư, Người thực thi và người nhận.
/// Người nhận chỉ thấy dòng của chính mình; luật sư thấy thêm danh sách tài sản, người nhận và biên bản phân chia của Chủ tài sản.
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

    /// <summary>Lý do luật sư hủy biên bản; null khi Chủ tài sản hủy hoặc chưa hủy.</summary>
    public string? CancelReason { get; set; }

    /// <summary>Kho đã khóa chưa: khóa khi luật sư chấp nhận giấy tờ đầu tiên; chưa khóa thì chưa chốt được biên bản.</summary>
    public bool VaultLocked { get; set; }

    /// <summary>
    /// Chỗ biên bản khác cách chia ban đầu của Chủ tài sản (luật sư, Người thực thi thấy; người nhận nhận danh sách rỗng).
    /// Có khác biệt thì luật sư phải tick confirmSplitDeviation khi chốt.
    /// </summary>
    public List<HandoverSplitDifferenceResponse> SplitDifferences { get; set; } = new List<HandoverSplitDifferenceResponse>();

    /// <summary>Lúc luật sư xác nhận khác biệt khi chốt; null khi không khác hoặc chưa chốt (mở lại thì về null).</summary>
    public DateTimeOffset? SplitDeviationConfirmedAt { get; set; }

    /// <summary>Họ tên luật sư đã xác nhận khác biệt; rỗng khi chưa xác nhận.</summary>
    public string SplitDeviationConfirmedByName { get; set; } = string.Empty;

    public string VerifierName { get; set; } = string.Empty;
    public string ExecutorName { get; set; } = string.Empty;
    public DateTimeOffset? VerifierSignedAt { get; set; }
    public DateTimeOffset? ExecutorSignedAt { get; set; }

    /// <summary>Lúc luật sư nhắc Người thực thi ký gần nhất; null khi chưa nhắc.</summary>
    public DateTimeOffset? ExecutorReminderSentAt { get; set; }

    /// <summary>Lúc luật sư nhắc lại được (lần nhắc trước + 24 giờ); null khi chưa nhắc lần nào.</summary>
    public DateTimeOffset? ExecutorRemindAvailableAt { get; set; }

    public List<HandoverRecordRecipientResponse> Recipients { get; set; } = new List<HandoverRecordRecipientResponse>();

    /// <summary>Người đang xem ký được lúc này không (đúng bước, chưa ký bản hiện tại).</summary>
    public bool CanSign { get; set; }

    /// <summary>Chỉ cho luật sư: tài sản của kho để chọn.</summary>
    public List<HandoverRecordAssetResponse> AvailableAssets { get; set; } = new List<HandoverRecordAssetResponse>();

    /// <summary>Chỉ cho luật sư: người nhận Chủ tài sản đã khai.</summary>
    public List<HandoverRecordPersonResponse> AvailableRecipients { get; set; } = new List<HandoverRecordPersonResponse>();

    /// <summary>Chỉ cho luật sư: biên bản phân chia của Chủ tài sản, gom theo người nhận. Rỗng khi Chủ tài sản chưa gán tài sản nào.</summary>
    public List<OwnerSplitRecipientResponse> OwnerSplit { get; set; } = new List<OwnerSplitRecipientResponse>();

    /// <summary>Yêu cầu nhận tài sản kèm giấy tờ; chỉ luật sư của biên bản thấy, các vai khác nhận danh sách rỗng.</summary>
    public List<HandoverClaimResponse> Claims { get; set; } = new List<HandoverClaimResponse>();

    /// <summary>Tệp căn cứ luật sư đính kèm biên bản; chỉ luật sư thấy.</summary>
    public List<HandoverRecordFileResponse> EvidenceFiles { get; set; } = new List<HandoverRecordFileResponse>();

    /// <summary>Hồ sơ báo tin đã chấp nhận gắn với biên bản; chỉ luật sư thấy.</summary>
    public List<HandoverRecordReportResponse> Reports { get; set; } = new List<HandoverRecordReportResponse>();
}

/// <summary>Một chỗ biên bản khác cách chia ban đầu của Chủ tài sản; chỉ có họ tên và tên tài sản, không có CCCD.</summary>
public class HandoverSplitDifferenceResponse
{
    /// <summary>RECIPIENT_ADDED, RECIPIENT_REMOVED hoặc ASSET_RECIPIENTS_CHANGED.</summary>
    public string Kind { get; set; } = string.Empty;

    public Guid? BeneficiaryId { get; set; }

    /// <summary>Họ tên người nhận được thêm hoặc bị bỏ; rỗng với khác biệt theo tài sản.</summary>
    public string RecipientName { get; set; } = string.Empty;

    public Guid? AssetId { get; set; }

    /// <summary>Tên tài sản đổi người nhận; rỗng với khác biệt theo người nhận.</summary>
    public string AssetName { get; set; } = string.Empty;

    /// <summary>Người nhận tài sản theo Chủ tài sản (chỉ với ASSET_RECIPIENTS_CHANGED).</summary>
    public List<string> OwnerRecipientNames { get; set; } = new List<string>();

    /// <summary>Người nhận tài sản theo biên bản (chỉ với ASSET_RECIPIENTS_CHANGED).</summary>
    public List<string> RecordRecipientNames { get; set; } = new List<string>();
}

/// <summary>Hồ sơ báo tin gắn với biên bản: người nộp, người nhận họ khai (chưa kiểm) và giấy tờ.</summary>
public class HandoverRecordReportResponse
{
    public Guid Id { get; set; }
    public string SubmitterName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<SuggestedRecipientResponse> Recipients { get; set; } = new List<SuggestedRecipientResponse>();
    public List<HandoverReportFileResponse> Files { get; set; } = new List<HandoverReportFileResponse>();
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

    /// <summary>Luật sư và Người thực thi ký riêng cho phần của người nhận này.</summary>
    public DateTimeOffset? VerifierSignedAt { get; set; }
    public DateTimeOffset? ExecutorSignedAt { get; set; }

    /// <summary>Người đang xem (luật sư hoặc Người thực thi) ký được cho người nhận này ngay lúc này.</summary>
    public bool CanSign { get; set; }

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

    /// <summary>Email đã khai; luật sư dùng để biết người trong hồ sơ báo tin đã có trong kho chưa.</summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>Một người nhận trong biên bản phân chia của Chủ tài sản và các tài sản Chủ tài sản đã gán cho người đó.</summary>
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
