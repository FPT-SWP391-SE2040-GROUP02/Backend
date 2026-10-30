namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả đặt lại tài khoản về lúc mới đăng ký: chỉ số lượng đã xóa, không có dữ liệu định danh.</summary>
public class DevEkycResetResponse
{
    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    /// <summary>Số lượt eKYC đã xóa (mọi mục đích).</summary>
    public int VerificationsRemoved { get; set; }

    /// <summary>Số lần đồng ý chụp ảnh đã xóa.</summary>
    public int ConsentsRemoved { get; set; }

    /// <summary>Có xóa danh tính đã gắn của Owner hay không.</summary>
    public bool IdentityRemoved { get; set; }

    /// <summary>Số phiên bản mẫu khuôn mặt của Owner đã xóa.</summary>
    public int FaceTemplatesRemoved { get; set; }

    /// <summary>Có xóa kho của tài khoản (kèm mọi dữ liệu trong kho) hay không; false khi tài khoản chưa có kho.</summary>
    public bool VaultRemoved { get; set; }

    /// <summary>Số tệp mã hóa đã xóa khỏi kho lưu trữ (tài sản, giấy chứng tử, ảnh selfie, ảnh CCCD).</summary>
    public int FilesRemoved { get; set; }
}
