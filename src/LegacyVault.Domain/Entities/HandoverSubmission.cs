namespace LegacyVault.Domain.Entities;

/// <summary>
/// Hồ sơ người nhận nộp cho Người xét duyệt (bảng HandoverSubmissions): lựa chọn Chấp nhận hoặc Từ chối, thông tin
/// khai trên văn bản mẫu và ảnh căn cước hai mặt. Thay cho xác minh eKYC bằng camera ở bước danh tính của lượt bàn giao.
/// </summary>
/// <remarks>
/// - Không lưu họ tên, ngày sinh: Backend đối chiếu bản khai với thông tin Chủ kho đã khai cho người nhận lúc nộp
///   và giữ kết quả khớp hay không, cộng số căn cước người nhận khai.
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

    /// <summary>Số căn cước đầy đủ người nhận khai, để Người xét duyệt đối chiếu với ảnh.</summary>
    public string CitizenId { get; set; } = string.Empty;

    /// <summary>Họ tên người nhận đã gõ lúc nộp, để luật sư xem cạnh thông tin Chủ kho khai. Rỗng với hồ sơ nộp trước khi có cột này.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Ngày sinh người nhận đã gõ lúc nộp; null với hồ sơ nộp trước khi có cột này.</summary>
    public DateOnly? DateOfBirth { get; set; }

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

    /// <summary>Lý do yêu cầu bổ sung (khi Status = SupplementRequested).</summary>
    public string? SupplementReason { get; set; }

    /// <summary>Người xét duyệt đã quyết định (duyệt hoặc yêu cầu bổ sung).</summary>
    public Guid? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
