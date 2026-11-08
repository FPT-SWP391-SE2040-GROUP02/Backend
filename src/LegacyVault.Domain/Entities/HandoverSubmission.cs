namespace LegacyVault.Domain.Entities;

/// <summary>Người nhận chọn gì trong hồ sơ nộp cho Người xét duyệt.</summary>
public enum HandoverSubmissionKind
{
    /// <summary>Chấp nhận nhận tài sản của gói.</summary>
    Accept = 0,

    /// <summary>Từ chối nhận; tài sản của gói coi như không giao cho ai.</summary>
    Decline = 1,
}

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

/// <summary>
/// Hồ sơ người nhận nộp cho Người xét duyệt (bảng HandoverSubmissions): lựa chọn Chấp nhận hoặc Từ chối, thông tin
/// khai trên văn bản mẫu và ảnh căn cước hai mặt. Thay cho xác minh eKYC bằng camera ở bước danh tính của lượt bàn giao.
/// </summary>
/// <remarks>
/// - Không lưu họ tên, ngày sinh hay số căn cước đầy đủ: Backend đối chiếu bản khai với thông tin Chủ kho đã khai
///   cho người nhận lúc nộp và chỉ giữ kết quả khớp hay không, cộng 4 số cuối.
/// - Ảnh căn cước lưu mã hóa (cùng cơ chế ảnh danh tính Chủ kho); chỉ Người xét duyệt được giao xem, mỗi lần xem có log.
/// - Mỗi lượt có thể có nhiều hồ sơ (nộp lại sau khi được yêu cầu bổ sung); chỉ bản mới nhất còn hiệu lực.
/// </remarks>
public class HandoverSubmission
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TurnId { get; set; }

    public HandoverTurn Turn { get; set; } = null!;

    public HandoverSubmissionKind Kind { get; set; }

    public HandoverSubmissionStatus Status { get; set; }

    /// <summary>Họ tên khai khớp thông tin Chủ kho đã khai cho người nhận (so không phân biệt dấu và hoa thường).</summary>
    public bool NameMatches { get; set; }

    /// <summary>Số căn cước khai khớp.</summary>
    public bool CitizenIdMatches { get; set; }

    /// <summary>Ngày sinh khai khớp.</summary>
    public bool DateOfBirthMatches { get; set; }

    /// <summary>4 số cuối căn cước khai, để Người xét duyệt đối chiếu với ảnh.</summary>
    public string CitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>Người nhận đã tick đọc điều khoản của lựa chọn.</summary>
    public bool Acknowledged { get; set; }

    /// <summary>Chỉ với Từ chối: tick tự nguyện từ chối và không nhằm trốn tránh nghĩa vụ tài sản.</summary>
    public bool VoluntaryDecline { get; set; }

    public string FrontImageStorageKey { get; set; } = string.Empty;

    public string FrontImageContentType { get; set; } = string.Empty;

    public long FrontImageSizeBytes { get; set; }

    public string FrontImageEncryptionKeyId { get; set; } = string.Empty;

    public byte[] FrontImageWrappedDataKey { get; set; } = Array.Empty<byte>();

    public string BackImageStorageKey { get; set; } = string.Empty;

    public string BackImageContentType { get; set; } = string.Empty;

    public long BackImageSizeBytes { get; set; }

    public string BackImageEncryptionKeyId { get; set; } = string.Empty;

    public byte[] BackImageWrappedDataKey { get; set; } = Array.Empty<byte>();

    /// <summary>Ảnh PNG văn bản từ chối, dựng khi Người xét duyệt duyệt hồ sơ Từ chối; rỗng khi không có.</summary>
    public string PaperStorageKey { get; set; } = string.Empty;

    public long PaperSizeBytes { get; set; }

    public string PaperEncryptionKeyId { get; set; } = string.Empty;

    public byte[] PaperWrappedDataKey { get; set; } = Array.Empty<byte>();

    /// <summary>Lý do yêu cầu bổ sung (khi Status = SupplementRequested).</summary>
    public string? SupplementReason { get; set; }

    /// <summary>Người xét duyệt đã quyết định (duyệt hoặc yêu cầu bổ sung).</summary>
    public Guid? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
