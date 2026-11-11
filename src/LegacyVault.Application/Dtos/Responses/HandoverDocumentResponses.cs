namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Phần 1 của biên bản: thông tin về Chủ kho, chép từ hồ sơ chứng tử đã duyệt. Không ai điền hay ký riêng.
/// Hồ sơ chứng tử không bắt buộc các trường này nên có thể trống (null hoặc chuỗi rỗng).
/// </summary>
public class HandoverDocumentOwnerPart
{
    public string FullName { get; set; } = string.Empty;

    public DateOnly? DateOfDeath { get; set; }

    public string CertificateNumber { get; set; } = string.Empty;

    public DateOnly? CertificateIssuedOn { get; set; }

    /// <summary>Nơi đăng ký khai tử dạng câu, ví dụ "UBND phường Bến Thành, Thành phố Hồ Chí Minh"; rỗng khi hồ sơ không ghi.</summary>
    public string DeathRegistrationPlace { get; set; } = string.Empty;

    /// <summary>Nơi thường trú theo danh tính eKYC đã xác minh của Chủ kho; rỗng khi không có.</summary>
    public string ResidenceAddress { get; set; } = string.Empty;
}

/// <summary>Phần 2 của biên bản, do người nhận điền và ký.</summary>
public class HandoverDocumentRecipientPart
{
    public string FullName { get; set; } = string.Empty;

    public int? BirthYear { get; set; }

    /// <summary>Số Căn cước/CCCD 12 số của người nhận (giải mã trong RAM từ hồ sơ người được chỉ định để hiển thị trên biên bản).</summary>
    public string CitizenId { get; set; } = string.Empty;

    public string CitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>Nơi thường trú người nhận khai; rỗng khi chưa ký.</summary>
    public string ResidenceAddress { get; set; } = string.Empty;

    public DateTimeOffset? SignedAt { get; set; }
}

/// <summary>Phần của Người xét duyệt: đã kiểm tra thủ công và chịu trách nhiệm.</summary>
public class HandoverDocumentVerifierPart
{
    public string SignedByName { get; set; } = string.Empty;

    /// <summary>Bốn mục kiểm tra đã tick, rỗng khi chưa kiểm tra.</summary>
    public List<bool> Checks { get; set; } = new List<bool>();

    public DateTimeOffset? SignedAt { get; set; }
}

/// <summary>Biên bản đồng ý nhận hoặc văn bản từ chối của một lượt, nhìn từ ba phía (người nhận, Người thực thi xem lại, Người xét duyệt).</summary>
public class HandoverDocumentResponse
{
    public Guid Id { get; set; }

    public Guid TurnId { get; set; }

    public Guid VaultId { get; set; }

    /// <summary>Mã ngắn để nhắc tới biên bản (8 ký tự cuối id, in hoa).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>ACCEPT | DECLINE.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>WAIT_RECIPIENT | WAIT_VERIFIER | COMPLETED.</summary>
    public string Status { get; set; } = string.Empty;

    public string TemplateVersion { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    public HandoverDocumentOwnerPart Owner { get; set; } = new HandoverDocumentOwnerPart();

    public HandoverDocumentRecipientPart Recipient { get; set; } = new HandoverDocumentRecipientPart();

    public HandoverDocumentVerifierPart Verifier { get; set; } = new HandoverDocumentVerifierPart();

    /// <summary>Lý do Người xét duyệt trả về người nhận, có khi biên bản đang chờ người nhận sửa.</summary>
    public string? SupplementReason { get; set; }

    /// <summary>Id hồ sơ nộp mới nhất của người nhận; Người xét duyệt dùng để xem ảnh căn cước. Null với người nhận và Người thực thi.</summary>
    public Guid? SubmissionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
