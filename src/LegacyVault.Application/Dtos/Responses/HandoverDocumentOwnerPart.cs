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
