namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một phiên bản khai báo của hồ sơ chứng tử (bảng DeathCaseVersions).
/// </summary>
/// <remarks>
/// - DEATH-04: bản có SubmittedAt (Executor đã xác nhận đúng) không bao giờ sửa; muốn đổi thì tạo bản mới (D5).
/// - DATA-02 (D6): họ tên Owner, CCCD Owner, số giấy, nơi cấp mã hóa chung một gói AES-256-GCM,
///   DEK mới mỗi lần lưu, AAD gắn Id phiên bản. Ngày mất, ngày cấp để rõ vì cần so sánh và kiểm điểm danh.
/// - Các trường mã hóa null khi Executor chưa nhập gì.
/// </remarks>
public class DeathCaseVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CaseId { get; set; }

    /// <summary>1, 2, 3... duy nhất trong một hồ sơ.</summary>
    public int VersionNo { get; set; }

    /// <summary>Id của KEK đã bọc WrappedDataKey; null khi chưa có dữ liệu mã hóa.</summary>
    public string? EncryptionKeyId { get; set; }

    /// <summary>DEK của phiên bản, đã bọc bằng KEK (60 byte). Không trả cho FE, không log.</summary>
    public byte[]? WrappedDataKey { get; set; }

    /// <summary>DATA-02: JSON {ownerFullName, ownerCitizenId, certificateNumber, issuedBy} đã mã hóa.</summary>
    public byte[]? IdentityEncrypted { get; set; }

    /// <summary>DATA-02: 4 số cuối CCCD Owner, dạng rõ; response chỉ trả trường này.</summary>
    public string? OwnerCitizenIdLast4 { get; set; }

    /// <summary>Ngày mất ghi trên giấy (theo lịch Việt Nam); dùng cho DEATH-10 (D7).</summary>
    public DateOnly? DateOfDeath { get; set; }

    /// <summary>Ngày cấp giấy chứng tử; phải ≥ ngày mất và ≤ hôm nay.</summary>
    public DateOnly? IssuedOn { get; set; }

    /// <summary>ConfigVersion của kế hoạch lúc gửi bản này (tham khảo; snapshot ghim lúc duyệt ở HandoverStarter).</summary>
    public int? PlanConfigVersion { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Executor đã xác nhận gửi bản này; null khi còn nháp.</summary>
    public Guid? SubmittedByUserId { get; set; }

    /// <summary>DEATH-04: có giá trị nghĩa là bản đã khóa, không sửa được nữa.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    public List<DeathCaseFile> Files { get; set; } = new List<DeathCaseFile>();
}
