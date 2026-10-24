namespace LegacyVault.Domain.Entities;

/// <summary>
/// Hồ sơ người nhận cấp kho (bảng Beneficiaries): danh bạ người nhận, dùng lại cho nhiều gói (QUOTA-04).
/// </summary>
/// <remarks>
/// - Dữ liệu cá nhân (§11.6 dòng "Người nhận", DATA-02) được mã hóa envelope giống Asset:
///   mỗi hồ sơ có DEK riêng, DB chỉ lưu DEK ĐÃ BỌC bằng KEK; KEK nằm trong cấu hình server, không nằm trong DB.
///   Lộ DB mà không có KEK thì không đọc được số CCCD, họ tên, ngày sinh, email, số điện thoại.
/// - Mỗi lần lưu (tạo/sửa) sinh DEK mới và mã hóa lại toàn bộ.
/// - Chỉ CitizenIdLast4 để dạng rõ: dùng để dò trùng CCCD trong kho và cho Owner tìm kiếm.
/// - Số CCCD không đổi được sau khi tạo; muốn đổi thì xóa hồ sơ và tạo lại.
/// - Tạo hồ sơ không chiếm suất: suất tính theo chỉ định còn hiệu lực (QUOTA-03).
/// TODO: Không có bảng phiên bản, sửa là ghi đè. Snapshot (M10) tự chép dữ liệu người nhận lúc ghim;
/// nếu nhóm cần lịch sử thì thêm bảng phiên bản sau.
/// </remarks>
public class Beneficiary
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VaultId { get; set; }

    public Vault Vault { get; set; } = null!;

    /// <summary>4 số cuối CCCD, dạng rõ (không đủ để định danh). Trả cho FE thay cho số đầy đủ (DATA-02).</summary>
    public string CitizenIdLast4 { get; set; } = string.Empty;

    /// <summary>
    /// HMAC-SHA256 của số CCCD (chỉ mục chống trùng, QUOTA-04); không đảo ngược được. Null với hồ sơ tạo trước khi có cột này.
    /// Unique theo (VaultId, CitizenIdHash) để hai request cùng CCCD không tạo hai hồ sơ.
    /// </summary>
    public byte[]? CitizenIdHash { get; set; }

    /// <summary>Id của KEK đã bọc WrappedDataKey (cho phép đổi KEK mà vẫn mở được hồ sơ cũ).</summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>DEK của hồ sơ, đã bọc bằng KEK (60 byte). Không trả cho FE, không log.</summary>
    public byte[] WrappedDataKey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Đã mã hóa: JSON {citizenId, fullName, dateOfBirth}. CCCD là dữ liệu định danh phải mã hóa (DATA-02, §11.6).
    /// </summary>
    public byte[] IdentityEncrypted { get; set; } = Array.Empty<byte>();

    /// <summary>Đã mã hóa: JSON {email, phone}. Thông tin liên hệ là dữ liệu cá nhân (§11.6).</summary>
    public byte[] ContactEncrypted { get; set; } = Array.Empty<byte>();

    /// <summary>Không còn dùng từ khi bỏ SETUP-19; giữ cột để không phải migration. Hồ sơ mới luôn null, hồ sơ cũ giữ nguyên giá trị.</summary>
    public DateTimeOffset? MinorAcknowledgedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Đổi sau mỗi lần sửa; dùng làm ETag chống ghi đè. Null khi mới tạo → etag "0".</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
