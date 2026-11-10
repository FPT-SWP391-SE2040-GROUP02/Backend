namespace LegacyVault.Application.Services;

/// <summary>Dữ liệu in lên văn bản từ chối hoặc biên bản đồng ý nhận (IsAcceptance); chỉ gồm thông tin hệ thống đã giữ, không có số căn cước đầy đủ.</summary>
public sealed class DeclinePaperData
{
    /// <summary>true: biên bản đồng ý nhận phần di sản; false: văn bản từ chối.</summary>
    public bool IsAcceptance { get; set; }

    public string TurnCode { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string OwnerFullName { get; set; } = string.Empty;

    /// <summary>Họ tên người từ chối lấy từ hồ sơ người được chỉ định mà Chủ kho đã khai.</summary>
    public string DeclinerFullName { get; set; } = string.Empty;

    public string CitizenIdLast4 { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; set; }

    public bool NameMatches { get; set; }

    public bool CitizenIdMatches { get; set; }

    public bool DateOfBirthMatches { get; set; }

    public string ReviewerFullName { get; set; } = string.Empty;

    public string? ReviewerLawyerCardNumber { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }

    /// <summary>Số giấy chứng tử chép từ hồ sơ chứng tử đã duyệt vào biên bản; rỗng với hồ sơ chưa có biên bản.</summary>
    public string CertificateNumber { get; set; } = string.Empty;

    /// <summary>Năm sinh người nhận khai trong biên bản; null thì để dòng chấm.</summary>
    public int? RecipientBirthYear { get; set; }

    /// <summary>Nơi thường trú người nhận khai trong biên bản; rỗng thì để dòng chấm.</summary>
    public string RecipientResidenceAddress { get; set; } = string.Empty;

    /// <summary>Nơi thường trú của Chủ kho theo danh tính eKYC đã xác minh; rỗng thì để dòng chấm.</summary>
    public string OwnerResidenceAddress { get; set; } = string.Empty;

    /// <summary>Số tài sản trong gói để in "(gồm N mục)"; 0 thì không in.</summary>
    public int ItemCount { get; set; }

    /// <summary>true khi dựng cho một biên bản đã đủ chữ ký người nhận và Người xét duyệt: có thêm câu gửi Người thực thi và khối chữ ký.</summary>
    public bool HasSignatures { get; set; }

    public DateTimeOffset? RecipientSignedAt { get; set; }

    /// <summary>Ngày mất ghi trong hồ sơ chứng tử đã duyệt; null khi hồ sơ không ghi.</summary>
    public DateOnly? DateOfDeath { get; set; }

    /// <summary>Ngày cấp giấy chứng tử (ngày đăng ký khai tử); null khi không đọc được.</summary>
    public DateOnly? DeathCertificateIssuedOn { get; set; }
}

/// <summary>Dựng ảnh PNG "Văn bản từ chối nhận di sản" (một trang); cài đặt cụ thể nằm ở Infrastructure.</summary>
public interface IDeclinePaperRenderer
{
    byte[] Render(DeclinePaperData data);
}
