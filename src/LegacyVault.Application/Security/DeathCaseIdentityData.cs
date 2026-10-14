namespace LegacyVault.Application.Security;

/// <summary>
/// Phần khai báo nhạy cảm của một phiên bản hồ sơ chứng tử ở dạng rõ, chỉ tồn tại trong RAM khi xử lý (DATA-02, D6).
/// </summary>
/// <remarks>
/// Không ghi log, không đưa vào message lỗi hay email (SEC-07, OPS-04). OwnerCitizenId không trả cho FE.
/// Mọi trường có thể null khi bản nháp chưa nhập.
/// </remarks>
public class DeathCaseIdentityData
{
    public string? OwnerFullName { get; set; }

    /// <summary>Đủ 12 chữ số, đã chuẩn hóa bằng CitizenIdRules.</summary>
    public string? OwnerCitizenId { get; set; }

    /// <summary>Số giấy chứng tử.</summary>
    public string? CertificateNumber { get; set; }

    /// <summary>Nơi cấp giấy chứng tử.</summary>
    public string? IssuedBy { get; set; }
}
