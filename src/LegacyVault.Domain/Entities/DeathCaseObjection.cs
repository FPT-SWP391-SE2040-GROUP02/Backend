using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Báo "Tôi còn sống" của Owner trên một hồ sơ chứng tử (bảng DeathCaseObjections), SRS §6.3 F2C.
/// </summary>
/// <remarks>
/// - ALIVE-01: báo là chặn ngay: hồ sơ sang Suspended, kế hoạch sang Blocked, grant đang có sang Blocked.
///   Các trạng thái trước lưu ở PreviousCaseStatus / PreviousPlanStatus để trả lại nếu phản đối bị bác.
/// - ALIVE-03, ALIVE-09: hạn 7 ngày tính từ FirstReportedAt; báo lặp chỉ tăng ReportCount, không đặt lại hạn,
///   không tạo dòng mới (chỉ mục unique có lọc: mỗi hồ sơ một dòng Open).
/// - BR-50 sửa 25/10/2026: giữ MÃ HÓA ảnh selfie của lần thử gần nhất (đạt hoặc không) để Verifier đối chiếu với ảnh
///   CCCD lúc eKYC; lần thử mới thay ảnh cũ. Lỗi kỹ thuật (chưa so được) không lưu ảnh.
/// </remarks>
public class DeathCaseObjection
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CaseId { get; set; }

    public Guid VaultId { get; set; }

    public Guid OwnerUserId { get; set; }

    public ObjectionStatus Status { get; set; }

    public DateTimeOffset FirstReportedAt { get; set; }

    public DateTimeOffset LastReportedAt { get; set; }

    public int ReportCount { get; set; }

    /// <summary>FirstReportedAt + 7 ngày: hết hạn thì không chụp selfie nữa, Verifier kết luận dựa trên hồ sơ.</summary>
    public DateTimeOffset SelfieDeadlineAt { get; set; }

    public DeathCaseStatus PreviousCaseStatus { get; set; }

    public PlanStatus PreviousPlanStatus { get; set; }

    public DateTimeOffset? SelfiePassedAt { get; set; }

    /// <summary>Số lần selfie không khớp trong cửa sổ 24 giờ hiện tại (EKYC-20); lỗi kỹ thuật không tính.</summary>
    public int SelfieFailedCount { get; set; }

    public DateTimeOffset? SelfieFailureWindowStartedAt { get; set; }

    /// <summary>
    /// Ảnh selfie của lần thử gần nhất, nằm ở kho lưu trữ private, mã hóa envelope như tài sản; các cột SelfieImage* chỉ
    /// giữ tham chiếu và khóa đã bọc. Null SelfieImageStorageKey = chưa có lần thử nào được so khớp.
    /// </summary>
    public string? SelfieImageStorageKey { get; set; }

    /// <summary>image/jpeg | image/png | image/webp, nhận theo nội dung thật của ảnh.</summary>
    public string? SelfieImageContentType { get; set; }

    public long? SelfieImageSizeBytes { get; set; }

    public string? SelfieImageSha256 { get; set; }

    public string? SelfieImageEncryptionKeyId { get; set; }

    /// <summary>DEK của ảnh, đã bọc bằng KEK. Không trả cho FE, không log.</summary>
    public byte[]? SelfieImageWrappedDataKey { get; set; }

    public DateTimeOffset? SelfieImageCapturedAt { get; set; }

    /// <summary>Kết quả so khớp của chính ảnh đang lưu với mẫu khuôn mặt; null khi chưa có ảnh.</summary>
    public bool? SelfieImageMatched { get; set; }

    /// <summary>Verifier phụ trách (người được giao hồ sơ); null khi chưa có ai (chờ phân công, LV-07).</summary>
    public Guid? VerifierUserId { get; set; }

    /// <summary>Hạn Verifier kết luận; null khi chưa đặt.</summary>
    public DateTimeOffset? VerifierDueAt { get; set; }

    public int InsufficientEvidenceCount { get; set; }

    public ObjectionConclusion? Conclusion { get; set; }

    public string? ConclusionReason { get; set; }

    public Guid? ConcludedByUserId { get; set; }

    public DateTimeOffset? ConcludedAt { get; set; }

    /// <summary>Concurrency token: báo lặp, selfie và kết luận cùng sửa một dòng.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
