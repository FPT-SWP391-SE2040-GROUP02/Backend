namespace LegacyVault.Application.Security;

/// <summary>
/// Dữ liệu cá nhân của người nhận ở dạng rõ, chỉ tồn tại trong RAM khi xử lý request (DATA-02).
/// </summary>
/// <remarks>Không ghi log, không đưa vào message lỗi (SEC-07). CitizenId không được trả cho FE.</remarks>
public class BeneficiaryPersonalData
{
    /// <summary>Đủ 12 chữ số, giữ số 0 đầu (EKYC-08).</summary>
    public string CitizenId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    /// <summary>
    /// Lời nhắn Chủ kho để lại cho người nhận (không bắt buộc, tối đa <see cref="MessageMaxLength"/> ký tự). Được gửi kèm thư mời nhận
    /// tài sản. Lưu trong phần liên hệ đã mã hóa nên không cần cột mới; hồ sơ cũ không có thì null.
    /// </summary>
    public string? Message { get; set; }

    public const int MessageMaxLength = 300;
}
