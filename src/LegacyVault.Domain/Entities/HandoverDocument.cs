using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Biên bản đồng ý nhận hoặc văn bản từ chối của một lượt bàn giao (bảng HandoverDocuments).
/// Hai người ký: người nhận (Phần 2) rồi Người xét duyệt kiểm tra thủ công. Phần 1 (thông tin về Chủ kho) không ai nhập lại:
/// chép từ phiên bản hồ sơ chứng tử mà Người xét duyệt đã duyệt lúc tạo biên bản.
/// </summary>
/// <remarks>
/// - Mỗi lượt có một biên bản; người nhận bị yêu cầu bổ sung thì biên bản quay về bước người nhận, không tạo bản mới.
/// - Phần 1 là bản chụp lúc tạo, để biên bản xem lại được kể cả khi hồ sơ chứng tử hay tài khoản Chủ kho bị dọn sau này.
///   Hồ sơ chứng tử chỉ bắt buộc có tệp giấy chứng tử nên ngày mất, số giấy, ngày cấp có thể trống; chỗ trống in thành dòng chấm.
/// - Chỉ lưu thông tin cần cho biên bản: không có số căn cước hay ngày sinh đầy đủ (chỉ 4 số cuối và năm sinh).
///   Nơi thường trú của hai bên được lưu để xác định đúng người; tạm thời chỉ lưu, chưa hiển thị trên giao diện hay in lên văn bản.
///   Ảnh căn cước nằm ở HandoverSubmission và bị xóa khi biên bản hoàn tất.
/// - Biên bản không bị xóa khi lượt bị dọn theo thời hạn lưu: lưu không giới hạn để xem lại.
/// - Không có cột hạn chót nào: mỗi bước chờ tới khi người của bước đó làm xong.
/// </remarks>
public class HandoverDocument
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TurnId { get; set; }

    public HandoverTurn Turn { get; set; } = null!;

    public Guid VaultId { get; set; }

    public HandoverSubmissionKind Kind { get; set; }

    public HandoverDocumentStatus Status { get; set; }

    /// <summary>Số tài sản trong gói lúc tạo biên bản, để in "(gồm N mục)". Không lưu tên từng mục.</summary>
    public int ItemCount { get; set; }

    /// <summary>Phiên bản mẫu chữ của biên bản lúc tạo, để biết văn bản đã ký dùng bản mẫu nào.</summary>
    public string TemplateVersion { get; set; } = string.Empty;

    // ---- Phần 1: thông tin về Chủ kho, chép từ hồ sơ chứng tử đã duyệt ----

    /// <summary>Họ tên Chủ kho ghi trong hồ sơ chứng tử; hồ sơ không ghi thì lấy tên tài khoản Chủ kho.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    public DateOnly? DateOfDeath { get; set; }

    public string CertificateNumber { get; set; } = string.Empty;

    public DateOnly? CertificateIssuedOn { get; set; }

    /// <summary>
    /// Nơi đăng ký khai tử dạng câu đã ghép, ví dụ "UBND phường Bến Thành, Thành phố Hồ Chí Minh". Chụp lúc tạo biên bản
    /// từ hồ sơ chứng tử đã duyệt; rỗng khi hồ sơ không ghi (in thành dòng chấm).
    /// </summary>
    public string DeathRegistrationPlace { get; set; } = string.Empty;

    /// <summary>
    /// Nơi thường trú của Chủ kho theo danh tính đã xác minh bằng eKYC (đọc từ căn cước, Chủ kho xác nhận).
    /// Rỗng khi Chủ kho chưa có danh tính đã gắn hoặc không giải mã được; in thành dòng chấm.
    /// </summary>
    public string OwnerResidenceAddress { get; set; } = string.Empty;

    // ---- Phần 2: người nhận ----

    public string RecipientFullName { get; set; } = string.Empty;

    public int? RecipientBirthYear { get; set; }

    public string RecipientCitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>Nơi thường trú người nhận khai trên biên bản. Tạm thời chỉ lưu, chưa hiển thị.</summary>
    public string RecipientResidenceAddress { get; set; } = string.Empty;

    public DateTimeOffset? RecipientSignedAt { get; set; }

    public string RecipientTermsVersion { get; set; } = string.Empty;

    /// <summary>Hồ sơ nộp mới nhất của người nhận (ảnh căn cước, kết quả đối chiếu); null khi chưa nộp.</summary>
    public Guid? SubmissionId { get; set; }

    /// <summary>Lý do Người xét duyệt trả về người nhận gần nhất; null khi chưa bị trả.</summary>
    public string? SupplementReason { get; set; }

    // ---- Người xét duyệt ----

    public Guid? VerifierUserId { get; set; }

    public DateTimeOffset? VerifierSignedAt { get; set; }

    /// <summary>Các mục kiểm tra thủ công đã tick, dạng chuỗi "11" (mục nào tick thì "1"); biên bản cũ có thể là "1111".</summary>
    public string VerifierChecks { get; set; } = string.Empty;

    public string VerifierTermsVersion { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
