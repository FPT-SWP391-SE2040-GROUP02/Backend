using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một lượt xác minh danh tính điện tử của người dùng (bảng EkycVerifications).
/// </summary>
/// <remarks>
/// - Chỉ lưu KẾT QUẢ. Không lưu ảnh giấy tờ, ảnh khuôn mặt, vector đặc trưng; ảnh chỉ đi qua dịch vụ eKYC
///   rồi bị bỏ. (Vector của Owner đăng ký đạt nằm ở bảng riêng OwnerFaceTemplates, mã hóa, không ở đây.) Họ tên, ngày sinh, địa chỉ đọc từ giấy tờ chỉ trả cho FE một lần, không ghi DB.
/// - Chỉ 4 số cuối CCCD để dạng rõ (giống Beneficiary.CitizenIdLast4), đủ để đối chiếu mà không định danh được.
/// - DocumentToken là mã tham chiếu của dịch vụ eKYC (giữ khuôn mặt trên giấy tờ trong RAM của dịch vụ đó,
///   tối đa 30 phút). Chỉ server biết, không trả cho FE, và bị xóa ngay khi đã so khớp (SEC-04).
/// - Mỗi lần người dùng đọc lại mặt trước là một dòng mới; dòng mới nhất là kết quả hiện hành.
/// </remarks>
public class EkycVerification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public EkycStatus Status { get; set; }

    /// <summary>Mục đích của lượt eKYC, lấy từ lần đồng ý gần nhất trước khi chụp (EKYC-01, EKYC-03).</summary>
    public EkycPurpose Purpose { get; set; }

    /// <summary>
    /// Lượt bàn giao đang đối chiếu khi Purpose là BeneficiaryClaim (EKYC-02); null ở mục đích khác.
    /// Không đặt khóa ngoại để module eKYC không phụ thuộc bảng của luồng bàn giao.
    /// </summary>
    public Guid? HandoverTurnId { get; set; }

    /// <summary>Kết quả đối chiếu với người được chỉ định; NotChecked khi không có gì để đối chiếu (MATCH-01..05).</summary>
    public EkycIdentityMatch IdentityMatch { get; set; }

    /// <summary>
    /// true khi đã đọc mặt sau và mã định danh trong MRZ trùng 4 số cuối mặt trước, tức hai mặt cùng một thẻ (EKYC-04).
    /// Thẻ cũ không có MRZ chứa số căn cước thì luôn false.
    /// </summary>
    public bool BackSideChecked { get; set; }

    /// <summary>4 số cuối CCCD đọc được từ mặt trước, dạng rõ. Rỗng khi OCR không đọc được số.</summary>
    public string CitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>Mã tham chiếu khuôn mặt của dịch vụ eKYC; null sau khi đã so khớp hoặc khi hết hạn.</summary>
    public string? DocumentToken { get; set; }

    /// <summary>Hạn dùng của DocumentToken (phía dịch vụ eKYC giữ 30 phút).</summary>
    public DateTimeOffset? DocumentTokenExpiresAt { get; set; }

    /// <summary>
    /// Chỉ với lượt OWNER_ENROLLMENT đang chờ selfie: Id của KEK đã bọc EnrollmentWrappedDataKey. Cùng hai cột dưới
    /// giữ bản MÃ HÓA của CCCD, họ tên, ngày sinh đọc từ mặt trước để gắn danh tính khi lượt đạt (EKYC-27), vì giấy tờ
    /// và selfie là hai request khác nhau mà FE không được gửi lại dữ liệu (EKYC-16). Xóa ngay khi lượt chốt kết quả
    /// hoặc bị lượt mới thay.
    /// </summary>
    public string? EnrollmentIdentityKeyId { get; set; }

    /// <summary>DEK của bản tạm, đã bọc bằng KEK (60 byte).</summary>
    public byte[]? EnrollmentWrappedDataKey { get; set; }

    /// <summary>Đã mã hóa: JSON {citizenId, fullName, dateOfBirth}.</summary>
    public byte[]? EnrollmentIdentityEncrypted { get; set; }

    /// <summary>
    /// Chỉ với lượt OWNER_ENROLLMENT đang chờ selfie: ảnh mặt trước CCCD đã lưu MÃ HÓA ở kho lưu trữ private (BR-48 sửa
    /// 25/10/2026). Lượt đạt thì chuyển tham chiếu sang OwnerIdentity; lượt thất bại hoặc bị lượt mới thay thì xóa nội dung.
    /// </summary>
    public string? PendingDocumentImageStorageKey { get; set; }

    public string? PendingDocumentImageContentType { get; set; }

    public long? PendingDocumentImageSizeBytes { get; set; }

    public string? PendingDocumentImageSha256 { get; set; }

    public string? PendingDocumentImageEncryptionKeyId { get; set; }

    /// <summary>DEK của ảnh, đã bọc bằng KEK. Không trả cho FE, không log.</summary>
    public byte[]? PendingDocumentImageWrappedDataKey { get; set; }

    /// <summary>Điểm cosine giữa selfie và ảnh giấy tờ; null khi chưa so khớp. Không phải xác suất.</summary>
    public double? FaceMatchScore { get; set; }

    /// <summary>Selfie có qua kiểm tra chống giả mạo ảnh đơn hay không; null khi chưa kiểm tra.</summary>
    public bool? SpoofCheckPassed { get; set; }

    /// <summary>
    /// Mã phiên thử thách camera của dịch vụ eKYC (EKYC-10); null khi chưa bắt đầu hoặc đã kết thúc.
    /// Chỉ server biết, không trả cho FE.
    /// </summary>
    public string? LivenessSessionId { get; set; }

    /// <summary>
    /// true khi lượt này đã qua thử thách camera: đủ động tác và chống giả mạo đều đạt (EKYC-14).
    /// Cả hai đường so khớp (selfie của phiên, hoặc ảnh selfie tĩnh) đều đòi cờ này trước khi so khớp mặt.
    /// </summary>
    public bool LivenessPassed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Lúc có kết quả cuối (Passed/Failed); null khi còn Pending.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
